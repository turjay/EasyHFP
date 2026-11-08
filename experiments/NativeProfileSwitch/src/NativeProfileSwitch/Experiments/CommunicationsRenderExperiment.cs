using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using NativeProfileSwitch.Diagnostics;
using NativeProfileSwitch.Interop;
using NativeProfileSwitch.Models;
using NativeProfileSwitch.Verification;
using static NativeProfileSwitch.Interop.CoreAudioInterop;

namespace NativeProfileSwitch.Experiments
{
    /// <summary>
    /// Step 4 — Experiment B: Shared-mode WASAPI render stream categorized with AudioCategory_Communications
    /// via IAudioClient2::SetClientProperties.
    /// Tests both pre-Start (SetClientProperties + Initialize only) and active silent rendering (AUDCLNT_BUFFERFLAGS_SILENT).
    /// </summary>
    public sealed class CommunicationsRenderExperiment
    {
        private readonly ProfileVerificationEngine _verifier = new();

        public ExperimentRunReport Execute(
            HeadsetEndpointGroup targetGroup,
            int holdDurationMs = 3000,
            int postReleaseObserveMs = 1000,
            bool startSilentRenderStream = true,
            CancellationToken cancellationToken = default)
        {
            var report = StructuredDiagnosticLogger.CreateInitializedReport("ExperimentB_CommunicationsRender", targetGroup);
            report.StreamDirection = "Render";
            report.StreamCategory = "AudioCategory_Communications";

            var renderEndpoint = targetGroup.RenderEndpoints.Find(e => e.RawState == DEVICE_STATE_ACTIVE)
                                 ?? (targetGroup.RenderEndpoints.Count > 0 ? targetGroup.RenderEndpoints[0] : null);
            var captureEndpoint = targetGroup.CaptureEndpoints.Find(e => e.RawState == DEVICE_STATE_ACTIVE)
                                  ?? (targetGroup.CaptureEndpoints.Count > 0 ? targetGroup.CaptureEndpoints[0] : null);

            report.SelectedRenderEndpointId = renderEndpoint?.EndpointId;
            report.SelectedCaptureEndpointId = captureEndpoint?.EndpointId;

            if (renderEndpoint == null)
            {
                report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                report.Errors.Add("No render endpoint found for the selected headset group.");
                report.EndTimeUtc = DateTime.UtcNow;
                return report;
            }

            if (targetGroup.IsBluetoothLeAudio)
            {
                report.Outcome = ExperimentOutcome.INCONCLUSIVE;
                report.Notes.Add("Target device uses Bluetooth LE Audio (TMAP/LC3); Classic A2DP/HFP switching does not apply.");
                report.EndTimeUtc = DateTime.UtcNow;
                return report;
            }

            var watchedIds = new List<string> { renderEndpoint.EndpointId };
            if (captureEndpoint != null) watchedIds.Add(captureEndpoint.EndpointId);

            using var notificationCollector = new EndpointNotificationCollector(watchedIds);

            // 1. Baseline verification before opening WASAPI render client
            report.BaselineVerification = _verifier.CaptureSnapshot(
                "Baseline",
                renderEndpoint.EndpointId,
                captureEndpoint?.EndpointId,
                notificationCollector.GetEventsSnapshot());

            if (renderEndpoint.RawState != DEVICE_STATE_ACTIVE)
            {
                report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                report.Errors.Add($"Render endpoint '{renderEndpoint.FriendlyName}' is not active (state={renderEndpoint.StateText}).");
                report.EndTimeUtc = DateTime.UtcNow;
                return report;
            }

            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? mmDevice = null;
            IAudioClient2? audioClient2 = null;
            IAudioRenderClient? renderClient = null;
            IntPtr mixFormatPtr = IntPtr.Zero;
            bool streamStarted = false;
            long totalSilentFramesSubmitted = 0;

            try
            {
                var comType = typeof(MMDeviceEnumeratorComObject);
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(comType)!;

                int hr = enumerator.GetDevice(renderEndpoint.EndpointId, out mmDevice);
                LogApiCall(report, "IMMDeviceEnumerator::GetDevice(Render)", hr, renderEndpoint.EndpointId);
                if (hr != S_OK || mmDevice == null)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"GetDevice(Render) failed: {FormatHResult(hr)}");
                    return report;
                }

                Guid iidAudioClient2 = IID_IAudioClient2;
                hr = mmDevice.Activate(ref iidAudioClient2, CLSCTX_ALL, IntPtr.Zero, out object clientObj);
                LogApiCall(report, "IMMDevice::Activate(IAudioClient2)", hr, "Activate IAudioClient2 on render endpoint");
                if (hr != S_OK || clientObj == null)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"Activate(IAudioClient2) failed: {FormatHResult(hr)}");
                    return report;
                }

                audioClient2 = (IAudioClient2)clientObj;

                // Step B1: SetClientProperties(AudioCategory_Communications) MUST precede GetMixFormat and Initialize
                var clientProps = new AudioClientProperties
                {
                    cbSize = (uint)Marshal.SizeOf<AudioClientProperties>(),
                    bIsOffload = false,
                    eCategory = AUDIO_STREAM_CATEGORY.AudioCategory_Communications,
                    Options = AUDCLNT_STREAMOPTIONS.AUDCLNT_STREAMOPTIONS_NONE
                };

                hr = audioClient2.SetClientProperties(ref clientProps);
                LogApiCall(report, "IAudioClient2::SetClientProperties(AudioCategory_Communications)", hr, $"cbSize={clientProps.cbSize}, bIsOffload=false");
                if (hr != S_OK)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"SetClientProperties failed: {FormatHResult(hr)}");
                    return report;
                }

                hr = audioClient2.GetMixFormat(out mixFormatPtr);
                LogApiCall(report, "IAudioClient2::GetMixFormat", hr, "Query mix format after AudioCategory_Communications");
                if (hr != S_OK || mixFormatPtr == IntPtr.Zero)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"GetMixFormat failed: {FormatHResult(hr)}");
                    return report;
                }

                var mixFormat = Marshal.PtrToStructure<WAVEFORMATEX>(mixFormatPtr);
                var fmtSummary = AudioFormatSummary.FromWaveFormat(mixFormat);
                report.Notes.Add($"Post-SetClientProperties(Communications) Render MixFormat: {fmtSummary.Summary}");

                long hnsBufferDuration = 1_000_000; // 100ms
                uint streamFlags = AUDCLNT_STREAMFLAGS_NOPERSIST;

                hr = audioClient2.Initialize(
                    AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                    streamFlags,
                    hnsBufferDuration,
                    0,
                    mixFormatPtr,
                    IntPtr.Zero);
                LogApiCall(report, "IAudioClient2::Initialize(SHARED, NOPERSIST)", hr, $"Format={fmtSummary.Summary}");
                if (hr != S_OK)
                {
                    // Fallback with AUTOCONVERTPCM if driver rejected its own Communications mix format in Initialize
                    streamFlags |= AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
                    hr = audioClient2.Initialize(
                        AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                        streamFlags,
                        hnsBufferDuration,
                        0,
                        mixFormatPtr,
                        IntPtr.Zero);
                    LogApiCall(report, "IAudioClient2::Initialize(SHARED, AUTOCONVERTPCM)", hr, $"Fallback Format={fmtSummary.Summary}");
                    if (hr != S_OK)
                    {
                        report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                        report.Errors.Add($"IAudioClient2::Initialize failed: {FormatHResult(hr)}");
                        return report;
                    }
                }

                // Observe whether SetClientProperties + Initialize alone (without Start) triggers any change
                var preStartEvents = notificationCollector.GetEventsSnapshot();
                report.Notes.Add($"Pre-Start (SetClientProperties + Initialize only) notification count: {preStartEvents.Count}");

                if (!startSilentRenderStream)
                {
                    report.ActiveStreamVerification = _verifier.CaptureSnapshot(
                        "InitializedWithoutStart",
                        renderEndpoint.EndpointId,
                        captureEndpoint?.EndpointId,
                        preStartEvents,
                        capturedFramesDeliveredByHardware: 0,
                        renderedFramesSubmitted: 0,
                        communicationsRenderStreamActive: false);

                    report.Outcome = ExperimentOutcome.API_SUCCESS_PROFILE_UNVERIFIED;
                    report.Notes.Add("Stream was initialized with AudioCategory_Communications but Start() was not called; session remains AudioSessionStateInactive.");
                    return report;
                }

                hr = audioClient2.GetBufferSize(out uint bufferFrameCount);
                LogApiCall(report, "IAudioClient2::GetBufferSize", hr, $"BufferFrames={bufferFrameCount}");
                if (hr != S_OK)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"GetBufferSize failed: {FormatHResult(hr)}");
                    return report;
                }

                Guid iidRenderClient = IID_IAudioRenderClient;
                hr = audioClient2.GetService(ref iidRenderClient, out object renderObj);
                LogApiCall(report, "IAudioClient2::GetService(IAudioRenderClient)", hr, "Acquire render buffer writer");
                if (hr != S_OK || renderObj == null)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"GetService(IAudioRenderClient) failed: {FormatHResult(hr)}");
                    return report;
                }

                renderClient = (IAudioRenderClient)renderObj;

                // Prime initial buffer with AUDCLNT_BUFFERFLAGS_SILENT before Start()
                if (renderClient.GetBuffer(bufferFrameCount, out _) == S_OK)
                {
                    renderClient.ReleaseBuffer(bufferFrameCount, AUDCLNT_BUFFERFLAGS_SILENT);
                    totalSilentFramesSubmitted += bufferFrameCount;
                }

                var startStopwatch = Stopwatch.StartNew();
                hr = audioClient2.Start();
                LogApiCall(report, "IAudioClient2::Start", hr, "Start silent AudioCategory_Communications render stream");
                if (hr != S_OK)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"IAudioClient2::Start failed: {FormatHResult(hr)}");
                    return report;
                }

                streamStarted = true;
                bool invalidatedDuringRun = false;

                var holdStopwatch = Stopwatch.StartNew();
                while (holdStopwatch.ElapsedMilliseconds < holdDurationMs && !cancellationToken.IsCancellationRequested)
                {
                    int padHr = audioClient2.GetCurrentPadding(out uint numFramesPadding);
                    if (padHr == AUDCLNT_E_DEVICE_INVALIDATED || padHr == AUDCLNT_E_RESOURCES_INVALIDATED || padHr == AUDCLNT_E_SERVICE_NOT_RUNNING)
                    {
                        LogApiCall(report, "IAudioClient2::GetCurrentPadding", padHr, "Device invalidated during render");
                        report.Errors.Add($"Render endpoint invalidated mid-stream: {FormatHResult(padHr)}");
                        invalidatedDuringRun = true;
                        break;
                    }

                    uint numFramesAvailable = bufferFrameCount > numFramesPadding
                        ? bufferFrameCount - numFramesPadding
                        : 0;

                    if (numFramesAvailable > 0)
                    {
                        int bufHr = renderClient.GetBuffer(numFramesAvailable, out _);
                        if (bufHr == S_OK)
                        {
                            renderClient.ReleaseBuffer(numFramesAvailable, AUDCLNT_BUFFERFLAGS_SILENT);
                            totalSilentFramesSubmitted += numFramesAvailable;
                        }
                        else if (bufHr == AUDCLNT_E_DEVICE_INVALIDATED || bufHr == AUDCLNT_E_RESOURCES_INVALIDATED)
                        {
                            invalidatedDuringRun = true;
                            break;
                        }
                    }

                    Thread.Sleep(20);
                }

                var eventsDuringStream = notificationCollector.GetEventsSnapshot();
                if (eventsDuringStream.Count > 0)
                {
                    var firstEvt = eventsDuringStream[0];
                    report.TransitionToHfpDurationMs = Math.Max(0, (firstEvt.TimestampUtc - report.StartTimeUtc).TotalMilliseconds);
                }

                report.Notes.Add($"Total silent Communications render frames submitted: {totalSilentFramesSubmitted}");

                // 2. Capture verification snapshot while Communications render stream is active
                report.ActiveStreamVerification = _verifier.CaptureSnapshot(
                    "ActiveStream",
                    renderEndpoint.EndpointId,
                    captureEndpoint?.EndpointId,
                    eventsDuringStream,
                    capturedFramesDeliveredByHardware: 0,
                    renderedFramesSubmitted: totalSilentFramesSubmitted,
                    communicationsRenderStreamActive: true);

                // 3. Stop and reset stream
                int stopHr = audioClient2.Stop();
                LogApiCall(report, "IAudioClient2::Stop", stopHr, "Stop silent Communications render stream");
                streamStarted = false;

                int resetHr = audioClient2.Reset();
                LogApiCall(report, "IAudioClient2::Reset", resetHr, "Reset render client");

                if (invalidatedDuringRun)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    return report;
                }
            }
            catch (Exception ex)
            {
                report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                report.Errors.Add($"Unhandled exception in Experiment B: {ex.GetType().Name}: {ex.Message}");
                return report;
            }
            finally
            {
                if (mixFormatPtr != IntPtr.Zero)
                {
                    CoTaskMemFree(mixFormatPtr);
                    mixFormatPtr = IntPtr.Zero;
                }
                if (renderClient != null)
                {
                    try { Marshal.ReleaseComObject(renderClient); } catch { }
                    renderClient = null;
                }
                if (audioClient2 != null)
                {
                    if (streamStarted)
                    {
                        try { audioClient2.Stop(); } catch { }
                    }
                    try { Marshal.ReleaseComObject(audioClient2); } catch { }
                    audioClient2 = null;
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
            }

            // 4. Observe post-release restoration
            var releaseWatch = Stopwatch.StartNew();
            if (postReleaseObserveMs > 0 && !cancellationToken.IsCancellationRequested)
            {
                notificationCollector.WaitForEvent(postReleaseObserveMs);
                int remainingMs = postReleaseObserveMs - (int)releaseWatch.ElapsedMilliseconds;
                if (remainingMs > 0)
                {
                    Thread.Sleep(Math.Min(remainingMs, 300));
                }
            }

            report.PostReleaseVerification = _verifier.CaptureSnapshot(
                "PostRelease",
                renderEndpoint.EndpointId,
                captureEndpoint?.EndpointId,
                notificationCollector.GetEventsSnapshot(),
                capturedFramesDeliveredByHardware: 0,
                renderedFramesSubmitted: 0,
                communicationsRenderStreamActive: false);

            report.ReturnToA2dpDurationMs = releaseWatch.Elapsed.TotalMilliseconds;
            report.NotificationEvents = notificationCollector.GetEventsSnapshot();

            // 5. Evaluate outcome strictly: never claim PROFILE_TRANSITION_OBSERVED solely from S_OK
            EvaluateOutcome(report);
            report.EndTimeUtc = DateTime.UtcNow;
            return report;
        }

        public static void EvaluateOutcome(ExperimentRunReport report)
        {
            if (report.Errors.Count > 0)
            {
                report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                return;
            }

            var active = report.ActiveStreamVerification;
            if (active == null)
            {
                report.Outcome = ExperimentOutcome.INCONCLUSIVE;
                return;
            }

            if (active.InferredProfile == InferredTransportProfile.HfpHandsFree &&
                (active.Confidence == VerificationConfidence.High_MultiSignalConfirmed ||
                 active.Confidence == VerificationConfidence.Medium_SessionAndFormatProxy))
            {
                report.Outcome = ExperimentOutcome.PROFILE_TRANSITION_OBSERVED;
            }
            else
            {
                // WASAPI calls returned S_OK, but no corroborating endpoint property/format/session signal confirmed HFP transport
                report.Outcome = ExperimentOutcome.API_SUCCESS_PROFILE_UNVERIFIED;
            }
        }

        private static void LogApiCall(ExperimentRunReport report, string apiName, int hr, string notes)
        {
            report.ApiCalls.Add(new ApiCallLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                ApiName = apiName,
                HResult = hr,
                HResultFormatted = FormatHResult(hr),
                Notes = notes
            });
        }
    }
}
