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
    /// Step 3 — Experiment A: Controlled shared-mode WASAPI microphone capture on the Bluetooth headset capture endpoint.
    /// Discards all captured frames in-place without storing or routing audio.
    /// </summary>
    public sealed class WasapiCaptureExperiment
    {
        private readonly ProfileVerificationEngine _verifier = new();

        public ExperimentRunReport Execute(
            HeadsetEndpointGroup targetGroup,
            int holdDurationMs = 3000,
            int postReleaseObserveMs = 1000,
            bool useCommunicationsCategoryOnCapture = true,
            CancellationToken cancellationToken = default)
        {
            var report = StructuredDiagnosticLogger.CreateInitializedReport("ExperimentA_WasapiCapture", targetGroup);
            report.StreamDirection = "Capture";
            report.StreamCategory = useCommunicationsCategoryOnCapture
                ? "AudioCategory_Communications"
                : "AudioCategory_Other";

            var captureEndpoint = targetGroup.CaptureEndpoints.Find(e => e.RawState == DEVICE_STATE_ACTIVE)
                                  ?? (targetGroup.CaptureEndpoints.Count > 0 ? targetGroup.CaptureEndpoints[0] : null);
            var renderEndpoint = targetGroup.RenderEndpoints.Find(e => e.RawState == DEVICE_STATE_ACTIVE)
                                 ?? (targetGroup.RenderEndpoints.Count > 0 ? targetGroup.RenderEndpoints[0] : null);

            report.SelectedCaptureEndpointId = captureEndpoint?.EndpointId;
            report.SelectedRenderEndpointId = renderEndpoint?.EndpointId;

            if (captureEndpoint == null)
            {
                report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                report.Errors.Add("No capture endpoint found for the selected headset group.");
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

            var watchedIds = new List<string> { captureEndpoint.EndpointId };
            if (renderEndpoint != null) watchedIds.Add(renderEndpoint.EndpointId);

            using var notificationCollector = new EndpointNotificationCollector(watchedIds);

            // 1. Baseline verification before opening WASAPI capture client
            report.BaselineVerification = _verifier.CaptureSnapshot(
                "Baseline",
                renderEndpoint?.EndpointId,
                captureEndpoint.EndpointId,
                notificationCollector.GetEventsSnapshot());

            if (captureEndpoint.RawState != DEVICE_STATE_ACTIVE)
            {
                report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                report.Errors.Add($"Capture endpoint '{captureEndpoint.FriendlyName}' is not active (state={captureEndpoint.StateText}).");
                report.EndTimeUtc = DateTime.UtcNow;
                return report;
            }

            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? mmDevice = null;
            IAudioClient2? audioClient = null;
            IAudioCaptureClient? captureClient = null;
            IntPtr mixFormatPtr = IntPtr.Zero;
            bool streamStarted = false;
            long totalFramesDelivered = 0;
            double? firstPacketLatencyMs = null;

            try
            {
                var comType = typeof(MMDeviceEnumeratorComObject);
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(comType)!;

                int hr = enumerator.GetDevice(captureEndpoint.EndpointId, out mmDevice);
                LogApiCall(report, "IMMDeviceEnumerator::GetDevice(Capture)", hr, captureEndpoint.EndpointId);
                if (hr != S_OK || mmDevice == null)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"GetDevice failed: {FormatHResult(hr)}");
                    return report;
                }

                Guid iidAudioClient2 = IID_IAudioClient2;
                hr = mmDevice.Activate(ref iidAudioClient2, CLSCTX_ALL, IntPtr.Zero, out object clientObj);
                LogApiCall(report, "IMMDevice::Activate(IAudioClient2)", hr, "Activate WASAPI client on capture endpoint");
                if (hr != S_OK || clientObj == null)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"Activate(IAudioClient2) failed: {FormatHResult(hr)}");
                    return report;
                }

                audioClient = (IAudioClient2)clientObj;

                if (useCommunicationsCategoryOnCapture)
                {
                    var props = new AudioClientProperties
                    {
                        cbSize = (uint)Marshal.SizeOf<AudioClientProperties>(),
                        bIsOffload = false,
                        eCategory = AUDIO_STREAM_CATEGORY.AudioCategory_Communications,
                        Options = AUDCLNT_STREAMOPTIONS.AUDCLNT_STREAMOPTIONS_NONE
                    };
                    hr = audioClient.SetClientProperties(ref props);
                    LogApiCall(report, "IAudioClient2::SetClientProperties(AudioCategory_Communications)", hr, "Pre-Initialize stream categorization");
                }

                hr = audioClient.GetMixFormat(out mixFormatPtr);
                LogApiCall(report, "IAudioClient::GetMixFormat", hr, "Query supported shared-mode capture mix format");
                if (hr != S_OK || mixFormatPtr == IntPtr.Zero)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"GetMixFormat failed: {FormatHResult(hr)}");
                    return report;
                }

                var mixFormat = Marshal.PtrToStructure<WAVEFORMATEX>(mixFormatPtr);
                var fmtSummary = AudioFormatSummary.FromWaveFormat(mixFormat);
                report.Notes.Add($"Capture MixFormat: {fmtSummary.Summary}");

                // 100ms buffer in 100-ns units (1,000,000)
                long hnsBufferDuration = 1_000_000;
                uint streamFlags = AUDCLNT_STREAMFLAGS_NOPERSIST;

                hr = audioClient.Initialize(
                    AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                    streamFlags,
                    hnsBufferDuration,
                    0,
                    mixFormatPtr,
                    IntPtr.Zero);
                LogApiCall(report, "IAudioClient::Initialize(SHARED, NOPERSIST)", hr, $"Format={fmtSummary.Summary}");
                if (hr != S_OK)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"IAudioClient::Initialize failed: {FormatHResult(hr)}");
                    return report;
                }

                Guid iidCaptureClient = IID_IAudioCaptureClient;
                hr = audioClient.GetService(ref iidCaptureClient, out object capObj);
                LogApiCall(report, "IAudioClient::GetService(IAudioCaptureClient)", hr, "Acquire packet reader");
                if (hr != S_OK || capObj == null)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"GetService(IAudioCaptureClient) failed: {FormatHResult(hr)}");
                    return report;
                }

                captureClient = (IAudioCaptureClient)capObj;

                var startStopwatch = Stopwatch.StartNew();
                hr = audioClient.Start();
                LogApiCall(report, "IAudioClient::Start", hr, "Start shared-mode capture stream");
                if (hr != S_OK)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add($"IAudioClient::Start failed: {FormatHResult(hr)}");
                    return report;
                }

                streamStarted = true;
                bool invalidatedDuringRun = false;

                // Drain packets continuously for holdDurationMs without storing or routing audio
                var holdStopwatch = Stopwatch.StartNew();
                while (holdStopwatch.ElapsedMilliseconds < holdDurationMs && !cancellationToken.IsCancellationRequested)
                {
                    int pktHr = captureClient.GetNextPacketSize(out uint packetFrames);
                    if (pktHr == AUDCLNT_E_DEVICE_INVALIDATED || pktHr == AUDCLNT_E_RESOURCES_INVALIDATED || pktHr == AUDCLNT_E_SERVICE_NOT_RUNNING)
                    {
                        LogApiCall(report, "IAudioCaptureClient::GetNextPacketSize", pktHr, "Device invalidated during capture");
                        report.Errors.Add($"Capture endpoint invalidated mid-stream: {FormatHResult(pktHr)}");
                        invalidatedDuringRun = true;
                        break;
                    }

                    while (packetFrames > 0)
                    {
                        int bufHr = captureClient.GetBuffer(
                            out _,
                            out uint numFramesToRead,
                            out uint dwFlags,
                            out _,
                            out _);

                        if (bufHr != S_OK && bufHr != AUDCLNT_S_BUFFER_EMPTY)
                        {
                            LogApiCall(report, "IAudioCaptureClient::GetBuffer", bufHr, "Error reading capture packet");
                            if (bufHr == AUDCLNT_E_DEVICE_INVALIDATED || bufHr == AUDCLNT_E_RESOURCES_INVALIDATED)
                            {
                                invalidatedDuringRun = true;
                            }
                            break;
                        }

                        if (numFramesToRead > 0)
                        {
                            totalFramesDelivered += numFramesToRead;
                            if (!firstPacketLatencyMs.HasValue && (dwFlags & AUDCLNT_BUFFERFLAGS_SILENT) == 0)
                            {
                                firstPacketLatencyMs = startStopwatch.Elapsed.TotalMilliseconds;
                            }
                            else if (!firstPacketLatencyMs.HasValue)
                            {
                                firstPacketLatencyMs = startStopwatch.Elapsed.TotalMilliseconds;
                            }
                        }

                        // Immediately discard captured frames without reading ppData
                        captureClient.ReleaseBuffer(numFramesToRead);

                        if (captureClient.GetNextPacketSize(out packetFrames) != S_OK)
                        {
                            break;
                        }
                    }

                    if (invalidatedDuringRun) break;
                    Thread.Sleep(15);
                }

                report.TransitionToHfpDurationMs = firstPacketLatencyMs;
                report.Notes.Add($"Total capture frames drained and discarded: {totalFramesDelivered}");

                // 2. Capture verification snapshot while stream is still active
                report.ActiveStreamVerification = _verifier.CaptureSnapshot(
                    "ActiveStream",
                    renderEndpoint?.EndpointId,
                    captureEndpoint.EndpointId,
                    notificationCollector.GetEventsSnapshot(),
                    capturedFramesDeliveredByHardware: totalFramesDelivered);

                // 3. Stop and reset stream
                int stopHr = audioClient.Stop();
                LogApiCall(report, "IAudioClient::Stop", stopHr, "Stop capture stream");
                streamStarted = false;

                int resetHr = audioClient.Reset();
                LogApiCall(report, "IAudioClient::Reset", resetHr, "Reset capture client buffer");

                if (invalidatedDuringRun)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    return report;
                }
            }
            catch (Exception ex)
            {
                report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                report.Errors.Add($"Unhandled exception in Experiment A: {ex.GetType().Name}: {ex.Message}");
                return report;
            }
            finally
            {
                // Deterministic release of all COM objects before post-release verification
                if (mixFormatPtr != IntPtr.Zero)
                {
                    CoTaskMemFree(mixFormatPtr);
                    mixFormatPtr = IntPtr.Zero;
                }
                if (captureClient != null)
                {
                    try { Marshal.ReleaseComObject(captureClient); } catch { }
                    captureClient = null;
                }
                if (audioClient != null)
                {
                    if (streamStarted)
                    {
                        try { audioClient.Stop(); } catch { }
                    }
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
            }

            // 4. Observe post-release restoration back to A2DP
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
                renderEndpoint?.EndpointId,
                captureEndpoint.EndpointId,
                notificationCollector.GetEventsSnapshot(),
                capturedFramesDeliveredByHardware: 0);

            report.ReturnToA2dpDurationMs = releaseWatch.Elapsed.TotalMilliseconds;
            report.NotificationEvents = notificationCollector.GetEventsSnapshot();

            // 5. Evaluate overall experiment outcome based on real evidence
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
            var post = report.PostReleaseVerification;

            if (active == null)
            {
                report.Outcome = ExperimentOutcome.INCONCLUSIVE;
                return;
            }

            if (active.InferredProfile == InferredTransportProfile.HfpHandsFree &&
                (active.Confidence == VerificationConfidence.High_MultiSignalConfirmed ||
                 active.Confidence == VerificationConfidence.Medium_SessionAndFormatProxy))
            {
                if (post != null && post.ExternalActiveCaptureSessions > 0)
                {
                    report.Outcome = ExperimentOutcome.PROFILE_TRANSITION_OBSERVED;
                    report.Notes.Add(
                        $"HFP was active during capture, and remained in HFP after release because external process(es) [{string.Join(", ", post.ExternalCaptureProcessIds)}] still own active capture session(s). Did NOT falsely claim A2DP restoration.");
                }
                else
                {
                    report.Outcome = ExperimentOutcome.PROFILE_TRANSITION_OBSERVED;
                }
            }
            else
            {
                // API calls succeeded (S_OK), but verification signals did not confirm HFP transition
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
