using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NativeProfileSwitch.Diagnostics;
using NativeProfileSwitch.Discovery;
using NativeProfileSwitch.Experiments;
using NativeProfileSwitch.Interop;
using NativeProfileSwitch.Models;
using NativeProfileSwitch.Verification;
using Xunit;

namespace NativeProfileSwitch.Tests
{
    public class ExperimentAndVerificationTests : IDisposable
    {
        private readonly string _tempDir;

        public ExperimentAndVerificationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"EasyHFP_Phase2Tests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [Fact]
        public void GroupEndpointsByPhysicalDevice_MatchesRenderAndCaptureByContainerId_AndDetectsWin11UnifiedTopology()
        {
            var containerId = Guid.NewGuid();
            var endpoints = new List<AudioEndpointInfo>
            {
                new AudioEndpointInfo
                {
                    EndpointId = "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}",
                    FriendlyName = "Headphones (MONITOR III A.N.C.)",
                    InterfaceFriendlyName = "MONITOR III A.N.C.",
                    DataFlow = "Render",
                    RawState = CoreAudioInterop.DEVICE_STATE_ACTIVE,
                    StateText = "Active",
                    ContainerId = containerId,
                    DeviceInstanceId = @"SWD\MMDEVAPI\{0.0.0.00000000}.{11111111}\BTHENUM_DEV_AABBCCDDEEFF",
                    IsBluetoothClassic = true
                },
                new AudioEndpointInfo
                {
                    EndpointId = "{0.0.1.00000000}.{22222222-2222-2222-2222-222222222222}",
                    FriendlyName = "Headset Microphone (MONITOR III A.N.C.)",
                    InterfaceFriendlyName = "MONITOR III A.N.C.",
                    DataFlow = "Capture",
                    RawState = CoreAudioInterop.DEVICE_STATE_ACTIVE,
                    StateText = "Active",
                    ContainerId = containerId,
                    DeviceInstanceId = @"SWD\MMDEVAPI\{0.0.1.00000000}.{22222222}\BTHHFENUM_DEV_AABBCCDDEEFF",
                    IsBluetoothClassic = true
                }
            };

            foreach (var ep in endpoints)
            {
                EndpointDiscoveryService.ClassifyBluetoothProperties(ep);
            }

            var service = new EndpointDiscoveryService();
            var groups = service.GroupEndpointsByPhysicalDevice(endpoints);

            Assert.Single(groups);
            var group = groups[0];
            Assert.Equal(containerId, group.ContainerId);
            Assert.Equal("PKEY_Device_ContainerId", group.AssociationMethod);
            Assert.Equal("AABBCCDDEEFF", group.BluetoothMac);
            Assert.True(group.IsBluetoothClassic);
            Assert.True(group.IsWindows11UnifiedEndpointTopology);
            Assert.False(group.HasAmbiguity);
            Assert.Single(group.RenderEndpoints);
            Assert.Single(group.CaptureEndpoints);
        }

        [Fact]
        public void GroupEndpointsByPhysicalDevice_DoesNotMergeSameFriendlyNameWithDifferentContainerIds_AndFlagsAmbiguity()
        {
            var container1 = Guid.NewGuid();
            var container2 = Guid.NewGuid();

            var endpoints = new List<AudioEndpointInfo>
            {
                new AudioEndpointInfo
                {
                    EndpointId = "render-1",
                    FriendlyName = "Headphones (WH-1000XM5)",
                    InterfaceFriendlyName = "WH-1000XM5",
                    DataFlow = "Render",
                    RawState = CoreAudioInterop.DEVICE_STATE_ACTIVE,
                    ContainerId = container1,
                    IsBluetoothClassic = true
                },
                new AudioEndpointInfo
                {
                    EndpointId = "render-2",
                    FriendlyName = "Headphones (WH-1000XM5)",
                    InterfaceFriendlyName = "WH-1000XM5",
                    DataFlow = "Render",
                    RawState = CoreAudioInterop.DEVICE_STATE_ACTIVE,
                    ContainerId = container2,
                    IsBluetoothClassic = true
                }
            };

            var service = new EndpointDiscoveryService();
            var groups = service.GroupEndpointsByPhysicalDevice(endpoints);

            // Must NOT merge two distinct physical containers just because their names are identical!
            Assert.Equal(2, groups.Count);
            Assert.All(groups, g =>
            {
                Assert.True(g.HasAmbiguity);
                Assert.Contains("Multiple distinct physical containers", g.AmbiguityNotes);
            });
        }

        [Fact]
        public void EvaluateEvidence_DistinguishesApiSuccessOnly_FromConfirmedHfpTransition()
        {
            var renderInfo = new AudioEndpointInfo
            {
                EndpointId = "render-1",
                RawState = CoreAudioInterop.DEVICE_STATE_ACTIVE,
                StateText = "Active",
                IsBluetoothClassic = true,
                DefaultMixFormat = new AudioFormatSummary { Channels = 2, SampleRateHz = 48000, BitsPerSample = 32 },
                CommunicationsMixFormat = new AudioFormatSummary { Channels = 1, SampleRateHz = 16000, BitsPerSample = 16 }
            };
            var captureInfo = new AudioEndpointInfo
            {
                EndpointId = "capture-1",
                RawState = CoreAudioInterop.DEVICE_STATE_ACTIVE,
                StateText = "Active",
                IsBluetoothClassic = true
            };

            // Case 1: Active capture session + hardware frames delivered -> High confidence HfpHandsFree
            var snapConfirmed = new ProfileVerificationSnapshot
            {
                SelfActiveCaptureSessions = 1
            };
            ProfileVerificationEngine.EvaluateEvidence(
                snapConfirmed,
                renderInfo,
                captureInfo,
                capturedFramesDeliveredByHardware: 4800,
                renderedFramesSubmitted: 0,
                communicationsRenderStreamActive: false);

            Assert.Equal(InferredTransportProfile.HfpHandsFree, snapConfirmed.InferredProfile);
            Assert.Equal(VerificationConfidence.High_MultiSignalConfirmed, snapConfirmed.Confidence);

            // Case 2: Communications render stream alone without format change or capture session -> Low_ApiReturnOnly -> API_SUCCESS_PROFILE_UNVERIFIED
            var snapRenderOnly = new ProfileVerificationSnapshot
            {
                SelfActiveCaptureSessions = 0,
                SelfActiveRenderSessions = 1,
                RenderDeviceFormat = new AudioFormatSummary { Channels = 2, SampleRateHz = 48000, BitsPerSample = 32 },
                ObservedPropertyChangeEventsCount = 0
            };
            ProfileVerificationEngine.EvaluateEvidence(
                snapRenderOnly,
                renderInfo,
                captureInfo,
                capturedFramesDeliveredByHardware: 0,
                renderedFramesSubmitted: 4800,
                communicationsRenderStreamActive: true);

            Assert.Equal(VerificationConfidence.Low_ApiReturnOnly, snapRenderOnly.Confidence);

            var report = new ExperimentRunReport
            {
                ActiveStreamVerification = snapRenderOnly
            };
            CommunicationsRenderExperiment.EvaluateOutcome(report);
            Assert.Equal(ExperimentOutcome.API_SUCCESS_PROFILE_UNVERIFIED, report.Outcome);
        }

        [Fact]
        public void TestD_WhenExternalAppOwnsMicrophone_NeverFalselyClaimsA2dpRestored()
        {
            var fakeExecutor = new FakeExperimentExecutor
            {
                SimulatedPostReleaseSnapshot = new ProfileVerificationSnapshot
                {
                    PhaseLabel = "PostRelease",
                    RenderStateText = "Active",
                    CaptureStateText = "Active",
                    SelfActiveCaptureSessions = 0,
                    ExternalActiveCaptureSessions = 1,
                    ExternalCaptureProcessIds = new List<uint> { 4242 },
                    InferredProfile = InferredTransportProfile.HfpHandsFree,
                    Confidence = VerificationConfidence.Medium_SessionAndFormatProxy
                }
            };

            var harness = new ExperimentHarness(fakeExecutor);
            var group = new HeadsetEndpointGroup
            {
                DisplayName = "Test Headset",
                IsBluetoothClassic = true
            };

            var report = harness.RunTestD_ExternalMicrophoneOwnerCheck(group, holdDurationMs: 10, postReleaseObserveMs: 10);

            Assert.Equal(ExperimentOutcome.PROFILE_TRANSITION_OBSERVED, report.Outcome);
            Assert.NotNull(report.PostReleaseVerification);
            Assert.Equal(InferredTransportProfile.HfpHandsFree, report.PostReleaseVerification!.InferredProfile);
            Assert.NotEqual(InferredTransportProfile.A2dpStereo, report.PostReleaseVerification.InferredProfile);
            Assert.Contains(report.Notes, n => n.Contains("A2DP restoration blocked by external mic owner"));
        }

        [Fact]
        public void TestC_DetectsLingeringSelfSessionLeak_AndFailsCleanupCheck()
        {
            var fakeExecutor = new FakeExperimentExecutor
            {
                SimulatedPostReleaseSnapshot = new ProfileVerificationSnapshot
                {
                    PhaseLabel = "PostRelease",
                    SelfActiveCaptureSessions = 1, // Simulated leak!
                    InferredProfile = InferredTransportProfile.HfpHandsFree,
                    Confidence = VerificationConfidence.Medium_SessionAndFormatProxy
                }
            };

            var harness = new ExperimentHarness(fakeExecutor);
            var group = new HeadsetEndpointGroup { DisplayName = "Test Headset", IsBluetoothClassic = true };

            var reports = harness.RunTestC_RepeatedSwitching(group, iterations: 2, holdDurationMs: 5, delayBetweenIterationsMs: 5);

            Assert.Equal(2, reports.Count);
            Assert.All(reports, r =>
            {
                Assert.Equal(ExperimentOutcome.TRANSITION_FAILED, r.Outcome);
                Assert.Contains(r.Errors, e => e.Contains("Resource leak detected"));
            });
        }

        [Fact]
        public void StructuredDiagnosticLogger_WritesJsonAndTextLogs()
        {
            var group = new HeadsetEndpointGroup
            {
                DisplayName = "MONITOR III A.N.C.",
                ContainerId = Guid.NewGuid(),
                BluetoothMac = "AABBCCDDEEFF",
                IsBluetoothClassic = true
            };

            var report = StructuredDiagnosticLogger.CreateInitializedReport("UnitTestExperiment", group);
            report.Outcome = ExperimentOutcome.PROFILE_TRANSITION_OBSERVED;
            report.TransitionToHfpDurationMs = 142.5;
            report.ReturnToA2dpDurationMs = 610.0;

            var (jsonPath, textPath) = StructuredDiagnosticLogger.SaveReport(report, _tempDir);

            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(textPath));

            string json = File.ReadAllText(jsonPath);
            Assert.Contains("PROFILE_TRANSITION_OBSERVED", json);
            Assert.Contains("MONITOR III A.N.C.", json);
        }

        [Fact]
        public void GroupEndpointsByPhysicalDevice_WhenHfpServiceDisabledAndNoCaptureEndpoint_FlagsPrerequisiteNoteAndExtractsAssociationMac()
        {
            var containerId = new Guid("79875b93-251e-57a6-bc41-5b7a46dfb7c0");
            var ep = new AudioEndpointInfo
            {
                EndpointId = "{0.0.0.00000000}.{7a440f74-5176-459d-8615-92f02bddb1b9}",
                FriendlyName = "Headphones (MONITOR III A.N.C.)",
                InterfaceFriendlyName = "MONITOR III A.N.C.",
                DataFlow = "Render",
                RawState = CoreAudioInterop.DEVICE_STATE_ACTIVE,
                StateText = "Active",
                ContainerId = containerId,
                DeviceInstanceId = @"{1}.BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001065A_PID&0017\9&BFCBF6&0&AABBCCDDEEFF_C00000000",
                EnumeratorName = "BTHENUM"
            };

            EndpointDiscoveryService.ClassifyBluetoothProperties(ep);

            Assert.Equal("AABBCCDDEEFF", ep.ExtractedBluetoothMac);
            Assert.Equal("Classic-BthA2dp", ep.BluetoothTransportKind);
            Assert.True(ep.IsBluetoothClassic);

            var service = new EndpointDiscoveryService();
            var groups = service.GroupEndpointsByPhysicalDevice(new[] { ep });

            Assert.Single(groups);
            var group = groups[0];
            Assert.Equal("AABBCCDDEEFF", group.BluetoothMac);
            Assert.False(group.IsWindows11UnifiedEndpointTopology);
            Assert.NotNull(group.AmbiguityNotes);
            Assert.Contains("0000111e-0000-1000-8000-00805f9b34fb", group.AmbiguityNotes);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        private sealed class FakeExperimentExecutor : IProfileExperimentExecutor
        {
            public ProfileVerificationSnapshot? SimulatedPostReleaseSnapshot { get; set; }

            public ExperimentRunReport RunCaptureExperiment(
                HeadsetEndpointGroup group,
                int holdDurationMs,
                int postReleaseObserveMs,
                CancellationToken cancellationToken = default)
            {
                var report = StructuredDiagnosticLogger.CreateInitializedReport("FakeCapture", group);
                report.ActiveStreamVerification = new ProfileVerificationSnapshot
                {
                    PhaseLabel = "ActiveStream",
                    SelfActiveCaptureSessions = 1,
                    InferredProfile = InferredTransportProfile.HfpHandsFree,
                    Confidence = VerificationConfidence.High_MultiSignalConfirmed
                };
                report.PostReleaseVerification = SimulatedPostReleaseSnapshot ?? new ProfileVerificationSnapshot
                {
                    PhaseLabel = "PostRelease",
                    SelfActiveCaptureSessions = 0,
                    ExternalActiveCaptureSessions = 0,
                    InferredProfile = InferredTransportProfile.A2dpStereo,
                    Confidence = VerificationConfidence.High_MultiSignalConfirmed
                };
                WasapiCaptureExperiment.EvaluateOutcome(report);
                report.EndTimeUtc = DateTime.UtcNow;
                return report;
            }

            public ExperimentRunReport RunCommunicationsRenderExperiment(
                HeadsetEndpointGroup group,
                int holdDurationMs,
                int postReleaseObserveMs,
                bool startSilentRenderStream = true,
                CancellationToken cancellationToken = default)
            {
                return RunCaptureExperiment(group, holdDurationMs, postReleaseObserveMs, cancellationToken);
            }
        }
    }
}
