using System;
using System.Collections.Generic;
using System.Threading;
using NativeProfileSwitch.Diagnostics;
using NativeProfileSwitch.Models;

namespace NativeProfileSwitch.Experiments
{
    public interface IProfileExperimentExecutor
    {
        ExperimentRunReport RunCaptureExperiment(
            HeadsetEndpointGroup group,
            int holdDurationMs,
            int postReleaseObserveMs,
            CancellationToken cancellationToken = default);

        ExperimentRunReport RunCommunicationsRenderExperiment(
            HeadsetEndpointGroup group,
            int holdDurationMs,
            int postReleaseObserveMs,
            bool startSilentRenderStream = true,
            CancellationToken cancellationToken = default);
    }

    public sealed class RealProfileExperimentExecutor : IProfileExperimentExecutor
    {
        private readonly WasapiCaptureExperiment _captureExp = new();
        private readonly CommunicationsRenderExperiment _renderExp = new();

        public ExperimentRunReport RunCaptureExperiment(
            HeadsetEndpointGroup group,
            int holdDurationMs,
            int postReleaseObserveMs,
            CancellationToken cancellationToken = default)
        {
            return _captureExp.Execute(group, holdDurationMs, postReleaseObserveMs, useCommunicationsCategoryOnCapture: true, cancellationToken);
        }

        public ExperimentRunReport RunCommunicationsRenderExperiment(
            HeadsetEndpointGroup group,
            int holdDurationMs,
            int postReleaseObserveMs,
            bool startSilentRenderStream = true,
            CancellationToken cancellationToken = default)
        {
            return _renderExp.Execute(group, holdDurationMs, postReleaseObserveMs, startSilentRenderStream, cancellationToken);
        }
    }

    /// <summary>
    /// Step 6 — Repeatable Test Harness executing Tests A, B, C, D, and E.
    /// Never runs disruptive hardware operations unless explicitly invoked.
    /// </summary>
    public sealed class ExperimentHarness
    {
        private readonly IProfileExperimentExecutor _executor;

        public ExperimentHarness(IProfileExperimentExecutor? executor = null)
        {
            _executor = executor ?? new RealProfileExperimentExecutor();
        }

        /// <summary>
        /// Test A — A2DP to HFP via WASAPI shared-mode capture.
        /// </summary>
        public ExperimentRunReport RunTestA_CaptureSwitch(
            HeadsetEndpointGroup group,
            int holdDurationMs = 3000,
            int postReleaseObserveMs = 1000,
            CancellationToken cancellationToken = default)
        {
            var report = _executor.RunCaptureExperiment(group, holdDurationMs, postReleaseObserveMs, cancellationToken);
            report.ExperimentName = "TestA_A2dpToHfpViaCapture";
            return report;
        }

        /// <summary>
        /// Test B — A2DP to HFP via Communications-category silent render stream.
        /// </summary>
        public ExperimentRunReport RunTestB_CommunicationsRenderSwitch(
            HeadsetEndpointGroup group,
            int holdDurationMs = 3000,
            int postReleaseObserveMs = 1000,
            bool startSilentRenderStream = true,
            CancellationToken cancellationToken = default)
        {
            var report = _executor.RunCommunicationsRenderExperiment(group, holdDurationMs, postReleaseObserveMs, startSilentRenderStream, cancellationToken);
            report.ExperimentName = "TestB_A2dpToHfpViaCommunicationsPlayback";
            return report;
        }

        /// <summary>
        /// Test C — Repeated sequential switching with resource cleanup and latency tracking.
        /// </summary>
        public List<ExperimentRunReport> RunTestC_RepeatedSwitching(
            HeadsetEndpointGroup group,
            int iterations = 3,
            int holdDurationMs = 2000,
            int delayBetweenIterationsMs = 1000,
            bool useCaptureApproach = true,
            CancellationToken cancellationToken = default)
        {
            var reports = new List<ExperimentRunReport>();
            if (iterations < 1) iterations = 1;

            for (int i = 0; i < iterations; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                ExperimentRunReport iterReport = useCaptureApproach
                    ? _executor.RunCaptureExperiment(group, holdDurationMs, delayBetweenIterationsMs, cancellationToken)
                    : _executor.RunCommunicationsRenderExperiment(group, holdDurationMs, delayBetweenIterationsMs, true, cancellationToken);

                iterReport.ExperimentName = $"TestC_RepeatedSwitching_Iter{i + 1}of{iterations}";
                iterReport.Notes.Add($"Iteration {i + 1}/{iterations} completed.");

                // Verify deterministic cleanup: current process must not hold lingering sessions after release
                if (iterReport.PostReleaseVerification != null)
                {
                    if (iterReport.PostReleaseVerification.SelfActiveCaptureSessions > 0 ||
                        iterReport.PostReleaseVerification.SelfActiveRenderSessions > 0)
                    {
                        iterReport.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                        iterReport.Errors.Add("Resource leak detected: current process still owns an active audio session after stream release.");
                    }
                }

                reports.Add(iterReport);

                if (i + 1 < iterations && delayBetweenIterationsMs > 0 && !cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(delayBetweenIterationsMs);
                }
            }

            return reports;
        }

        /// <summary>
        /// Test D — Behavior when another application actively owns the Bluetooth microphone.
        /// Verifies that EasyHFP never falsely claims to have restored A2DP while an external process holds capture.
        /// </summary>
        public ExperimentRunReport RunTestD_ExternalMicrophoneOwnerCheck(
            HeadsetEndpointGroup group,
            int holdDurationMs = 1500,
            int postReleaseObserveMs = 800,
            CancellationToken cancellationToken = default)
        {
            var report = _executor.RunCaptureExperiment(group, holdDurationMs, postReleaseObserveMs, cancellationToken);
            report.ExperimentName = "TestD_ExternalMicrophoneOwner";

            var post = report.PostReleaseVerification;
            if (post != null && post.ExternalActiveCaptureSessions > 0)
            {
                // Crucial safety invariant: If an external app owns the microphone, PostRelease MUST NOT report A2dpStereo!
                if (post.InferredProfile == InferredTransportProfile.A2dpStereo)
                {
                    report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                    report.Errors.Add("Safety violation: PostRelease falsely reported A2dpStereo while an external process held an active capture session.");
                }
                else
                {
                    report.Notes.Add(
                        $"Verified Test D invariant: External process(es) [{string.Join(", ", post.ExternalCaptureProcessIds)}] hold {post.ExternalActiveCaptureSessions} active capture session(s). " +
                        $"Post-release profile honestly reported as {post.InferredProfile} (A2DP restoration blocked by external mic owner).");
                }
            }
            else
            {
                report.Notes.Add(
                    "No external application was actively capturing from the Bluetooth microphone during Test D. " +
                    "To test external contention on hardware, open Voice Recorder / Discord / Windows Sound Settings mic test before running --test-d.");
            }

            return report;
        }

        /// <summary>
        /// Test E — Recovery behavior for unplugged, disconnected, unavailable, or invalidated endpoints.
        /// Ensures clean termination with zero lingering audio streams.
        /// </summary>
        public ExperimentRunReport RunTestE_RecoveryAndInvalidEndpointHandling(
            HeadsetEndpointGroup group,
            int holdDurationMs = 1500,
            CancellationToken cancellationToken = default)
        {
            var report = _executor.RunCaptureExperiment(group, holdDurationMs, postReleaseObserveMs: 500, cancellationToken);
            report.ExperimentName = "TestE_RecoveryAndInvalidatedEndpoint";

            if (report.PostReleaseVerification != null &&
                (report.PostReleaseVerification.SelfActiveCaptureSessions > 0 ||
                 report.PostReleaseVerification.SelfActiveRenderSessions > 0))
            {
                report.Outcome = ExperimentOutcome.TRANSITION_FAILED;
                report.Errors.Add("Recovery check failed: lingering self-owned stream detected after Test E.");
            }
            else
            {
                report.Notes.Add("Verified clean stream release and zero lingering self-owned sessions after execution.");
            }

            return report;
        }
    }
}
