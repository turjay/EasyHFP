using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EasyHFP.Interop;
using static EasyHFP.Interop.NativeCoreAudio;

namespace EasyHFP.Services
{
    /// <summary>
    /// Holds a shared-mode WASAPI capture stream open on a Bluetooth headset's capture endpoint
    /// to keep Windows 11 in Hands-Free Profile (HFP) mode.
    /// All captured packets are discarded immediately in-place without reading, copying, or storing audio.
    /// </summary>
    public sealed class WasapiCaptureHoldSession : IWasapiCaptureHoldSession
    {
        private readonly ManualResetEventSlim _stopSignal = new(false);
        private readonly TaskCompletionSource<WasapiHoldStartResult> _startTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _workerCompletedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Thread? _workerThread;
        private int _startCalled;
        private int _disposed;
        private volatile bool _isRunning;
        private long _totalFramesDiscarded;

        public string TargetMac { get; }
        public string CaptureEndpointId { get; }
        public bool IsRunning => _isRunning;
        public long TotalFramesDiscarded => Interlocked.Read(ref _totalFramesDiscarded);

        public event Action<string>? SessionInvalidated;

        public WasapiCaptureHoldSession(string targetMac, string captureEndpointId)
        {
            TargetMac = targetMac ?? throw new ArgumentNullException(nameof(targetMac));
            CaptureEndpointId = captureEndpointId ?? throw new ArgumentNullException(nameof(captureEndpointId));
        }

        public Task<WasapiHoldStartResult> StartAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _startCalled, 1) != 0)
            {
                return _startTcs.Task;
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => _stopSignal.Set());
            }

            _workerThread = new Thread(CaptureHoldWorkerLoop)
            {
                Name = $"EasyHFP-WasapiHold-{TargetMac}",
                IsBackground = true
            };
            _workerThread.SetApartmentState(ApartmentState.MTA);
            _workerThread.Start();

            return _startTcs.Task;
        }

        public async Task StopAndReleaseAsync()
        {
            _stopSignal.Set();
            if (Volatile.Read(ref _startCalled) != 0)
            {
                await _workerCompletedTcs.Task.ConfigureAwait(false);
            }
        }

        private void CaptureHoldWorkerLoop()
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? mmDevice = null;
            IAudioClient2? audioClient = null;
            IAudioCaptureClient? captureClient = null;
            IntPtr mixFormatPtr = IntPtr.Zero;
            bool streamStarted = false;
            string? invalidationReason = null;

            try
            {
                var comType = typeof(MMDeviceEnumeratorComObject);
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(comType)!;

                int hr = enumerator.GetDevice(CaptureEndpointId, out mmDevice);
                if (hr != S_OK || mmDevice == null)
                {
                    _startTcs.TrySetResult(new WasapiHoldStartResult(
                        Started: false,
                        HResult: hr,
                        InitialFramesDiscarded: 0,
                        FirstPacketLatencyMs: 0,
                        ErrorMessage: $"IMMDeviceEnumerator::GetDevice failed: {FormatHResult(hr)}"));
                    return;
                }

                Guid iidAudioClient2 = IID_IAudioClient2;
                hr = mmDevice.Activate(ref iidAudioClient2, CLSCTX_ALL, IntPtr.Zero, out object clientObj);
                if (hr != S_OK || clientObj == null)
                {
                    _startTcs.TrySetResult(new WasapiHoldStartResult(
                        Started: false,
                        HResult: hr,
                        InitialFramesDiscarded: 0,
                        FirstPacketLatencyMs: 0,
                        ErrorMessage: $"IMMDevice::Activate(IAudioClient2) failed: {FormatHResult(hr)}"));
                    return;
                }

                audioClient = (IAudioClient2)clientObj;

                var props = new AudioClientProperties
                {
                    cbSize = (uint)Marshal.SizeOf<AudioClientProperties>(),
                    bIsOffload = false,
                    eCategory = AUDIO_STREAM_CATEGORY.AudioCategory_Communications,
                    Options = AUDCLNT_STREAMOPTIONS.AUDCLNT_STREAMOPTIONS_NONE
                };
                // Best-effort categorization prior to GetMixFormat/Initialize
                audioClient.SetClientProperties(ref props);

                hr = audioClient.GetMixFormat(out mixFormatPtr);
                if (hr != S_OK || mixFormatPtr == IntPtr.Zero)
                {
                    _startTcs.TrySetResult(new WasapiHoldStartResult(
                        Started: false,
                        HResult: hr,
                        InitialFramesDiscarded: 0,
                        FirstPacketLatencyMs: 0,
                        ErrorMessage: $"IAudioClient::GetMixFormat failed: {FormatHResult(hr)}"));
                    return;
                }

                // 100ms shared-mode buffer (in 100-ns units); NOPERSIST avoids saving per-app volume/session entries
                const long hnsBufferDuration = 1_000_000;
                hr = audioClient.Initialize(
                    AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                    AUDCLNT_STREAMFLAGS_NOPERSIST,
                    hnsBufferDuration,
                    0,
                    mixFormatPtr,
                    IntPtr.Zero);

                if (hr != S_OK)
                {
                    _startTcs.TrySetResult(new WasapiHoldStartResult(
                        Started: false,
                        HResult: hr,
                        InitialFramesDiscarded: 0,
                        FirstPacketLatencyMs: 0,
                        ErrorMessage: $"IAudioClient::Initialize failed: {FormatHResult(hr)}"));
                    return;
                }

                Guid iidCaptureClient = IID_IAudioCaptureClient;
                hr = audioClient.GetService(ref iidCaptureClient, out object capObj);
                if (hr != S_OK || capObj == null)
                {
                    _startTcs.TrySetResult(new WasapiHoldStartResult(
                        Started: false,
                        HResult: hr,
                        InitialFramesDiscarded: 0,
                        FirstPacketLatencyMs: 0,
                        ErrorMessage: $"IAudioClient::GetService(IAudioCaptureClient) failed: {FormatHResult(hr)}"));
                    return;
                }

                captureClient = (IAudioCaptureClient)capObj;

                var startStopwatch = Stopwatch.StartNew();
                hr = audioClient.Start();
                if (hr != S_OK)
                {
                    _startTcs.TrySetResult(new WasapiHoldStartResult(
                        Started: false,
                        HResult: hr,
                        InitialFramesDiscarded: 0,
                        FirstPacketLatencyMs: 0,
                        ErrorMessage: $"IAudioClient::Start failed: {FormatHResult(hr)}"));
                    return;
                }

                streamStarted = true;
                _isRunning = true;

                // Phase 1: Wait up to 2000ms for initial hardware packets from BthHfAud.sys to confirm HFP SCO link
                double? firstPacketLatencyMs = null;
                var initialWaitWatch = Stopwatch.StartNew();

                while (initialWaitWatch.ElapsedMilliseconds < 2000 && !_stopSignal.IsSet)
                {
                    if (!DrainAndDiscardAvailablePackets(captureClient, out int drainHr, out long framesJustDiscarded))
                    {
                        _isRunning = false;
                        _startTcs.TrySetResult(new WasapiHoldStartResult(
                            Started: false,
                            HResult: drainHr,
                            InitialFramesDiscarded: 0,
                            FirstPacketLatencyMs: 0,
                            ErrorMessage: $"Capture endpoint invalidated during startup: {FormatHResult(drainHr)}"));
                        return;
                    }

                    if (framesJustDiscarded > 0 && !firstPacketLatencyMs.HasValue)
                    {
                        firstPacketLatencyMs = startStopwatch.Elapsed.TotalMilliseconds;
                        break;
                    }

                    _stopSignal.Wait(15);
                }

                if (_stopSignal.IsSet && !firstPacketLatencyMs.HasValue)
                {
                    _isRunning = false;
                    _startTcs.TrySetResult(new WasapiHoldStartResult(
                        Started: false,
                        HResult: S_FALSE,
                        InitialFramesDiscarded: 0,
                        FirstPacketLatencyMs: 0,
                        ErrorMessage: "Capture startup was canceled before packets arrived."));
                    return;
                }

                long initialFrames = Interlocked.Read(ref _totalFramesDiscarded);
                _startTcs.TrySetResult(new WasapiHoldStartResult(
                    Started: true,
                    HResult: S_OK,
                    InitialFramesDiscarded: initialFrames,
                    FirstPacketLatencyMs: firstPacketLatencyMs ?? startStopwatch.Elapsed.TotalMilliseconds));

                // Phase 2: Steady-state hold loop — drain and immediately discard packets until stopped or invalidated
                while (!_stopSignal.Wait(20))
                {
                    if (!DrainAndDiscardAvailablePackets(captureClient, out int drainHr, out _))
                    {
                        invalidationReason = $"Capture stream invalidated ({FormatHResult(drainHr)})";
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Unhandled exception in WasapiCaptureHoldSession for {TargetMac}.", ex);
                _startTcs.TrySetResult(new WasapiHoldStartResult(
                    Started: false,
                    HResult: -1,
                    InitialFramesDiscarded: 0,
                    FirstPacketLatencyMs: 0,
                    ErrorMessage: ex.Message));
                invalidationReason = ex.Message;
            }
            finally
            {
                _isRunning = false;

                if (mixFormatPtr != IntPtr.Zero)
                {
                    try { CoTaskMemFree(mixFormatPtr); } catch { }
                    mixFormatPtr = IntPtr.Zero;
                }

                if (audioClient != null && streamStarted)
                {
                    try { audioClient.Stop(); } catch { }
                    try { audioClient.Reset(); } catch { }
                    streamStarted = false;
                }

                if (captureClient != null)
                {
                    try { Marshal.ReleaseComObject(captureClient); } catch { }
                    captureClient = null;
                }

                if (audioClient != null)
                {
                    try { Marshal.ReleaseComObject(audioClient); } catch { }
                    audioClient = null;
                }

                if (mmDevice != null)
                {
                    try { Marshal.ReleaseComObject(mmDevice); } catch { }
                    mmDevice = null;
                }

                if (enumerator != null)
                {
                    try { Marshal.ReleaseComObject(enumerator); } catch { }
                    enumerator = null;
                }

                _workerCompletedTcs.TrySetResult(true);

                if (invalidationReason != null)
                {
                    var handler = SessionInvalidated;
                    if (handler != null)
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try { handler(invalidationReason); } catch { }
                        });
                    }
                }
            }
        }

        private bool DrainAndDiscardAvailablePackets(
            IAudioCaptureClient captureClient,
            out int lastHr,
            out long framesDiscardedInCall)
        {
            framesDiscardedInCall = 0;
            lastHr = captureClient.GetNextPacketSize(out uint packetFrames);
            if (IsInvalidatedHResult(lastHr))
            {
                return false;
            }
            if (lastHr != S_OK)
            {
                return true;
            }

            while (packetFrames > 0)
            {
                uint numFramesToRead = 0;
                int bufHr = captureClient.GetBuffer(
                    out _, // Never read or copy ppData
                    out numFramesToRead,
                    out _,
                    out _,
                    out _);

                if (bufHr >= 0)
                {
                    try
                    {
                        if (numFramesToRead > 0)
                        {
                            framesDiscardedInCall += numFramesToRead;
                            Interlocked.Add(ref _totalFramesDiscarded, numFramesToRead);
                        }
                    }
                    finally
                    {
                        // Immediately release buffer back to WASAPI without touching audio bytes
                        captureClient.ReleaseBuffer(numFramesToRead);
                    }
                }
                else
                {
                    lastHr = bufHr;
                    if (IsInvalidatedHResult(bufHr))
                    {
                        return false;
                    }
                    break;
                }

                lastHr = captureClient.GetNextPacketSize(out packetFrames);
                if (IsInvalidatedHResult(lastHr))
                {
                    return false;
                }
                if (lastHr != S_OK)
                {
                    break;
                }
            }

            return true;
        }

        private static bool IsInvalidatedHResult(int hr)
        {
            return hr == AUDCLNT_E_DEVICE_INVALIDATED ||
                   hr == AUDCLNT_E_RESOURCES_INVALIDATED ||
                   hr == AUDCLNT_E_SERVICE_NOT_RUNNING;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stopSignal.Set();

            if (Volatile.Read(ref _startCalled) != 0)
            {
                try
                {
                    _workerCompletedTcs.Task.Wait(1500);
                }
                catch { }
            }

            _stopSignal.Dispose();
        }
    }
}
