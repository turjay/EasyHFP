using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EasyHFP.Interop;
using EasyHFP.Models;
using EasyHFP.Services;
using Xunit;

namespace EasyHFP.Tests
{
    public class WindowsAudioProfileControllerTests
    {
        [Fact]
        public void CoreAudioEndpointMapper_MatchesCaptureEndpointViaSharedContainerId_WhenMacIsOnRenderEndpoint()
        {
            var containerId = new Guid("79875b93-251e-57a6-bc41-5b7a46dfb7c0");
            var endpoints = new List<DiscoveredCoreAudioEndpoint>
            {
                new DiscoveredCoreAudioEndpoint
                {
                    EndpointId = "{0.0.0.00000000}.{7a440f74-5176-459d-8615-92f02bddb1b9}",
                    FriendlyName = "Headphones (MONITOR III A.N.C.)",
                    InterfaceFriendlyName = "MONITOR III A.N.C.",
                    DataFlow = NativeCoreAudio.EDataFlow.eRender,
                    RawState = NativeCoreAudio.DEVICE_STATE_ACTIVE,
                    ContainerId = containerId,
                    DeviceInstanceId = @"{1}.BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001065A_PID&0017\9&BFCBF6&0&AABBCCDDEEFF_C00000000",
                    EnumeratorName = "BTHENUM",
                    ExtractedBluetoothMac = CoreAudioEndpointMapper.ExtractBluetoothMac(
                        @"{1}.BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001065A_PID&0017\9&BFCBF6&0&AABBCCDDEEFF_C00000000")
                },
                new DiscoveredCoreAudioEndpoint
                {
                    EndpointId = "{0.0.1.00000000}.{ca2da733-6c03-4225-8b02-58946b8b8040}",
                    FriendlyName = "Headset (MONITOR III A.N.C.)",
                    InterfaceFriendlyName = "MONITOR III A.N.C.",
                    DataFlow = NativeCoreAudio.EDataFlow.eCapture,
                    RawState = NativeCoreAudio.DEVICE_STATE_ACTIVE,
                    ContainerId = containerId,
                    DeviceInstanceId = @"{1}.BTHHFENUM\BTHHFPAUDIO\A&15F9384&2B&97",
                    EnumeratorName = "BTHHFENUM",
                    ExtractedBluetoothMac = null,
                    SelfActiveSessionCount = 0,
                    ExternalActiveSessionCount = 0
                }
            };

            var map = CoreAudioEndpointMapper.ResolveFromDiscoveredEndpoints(
                endpoints,
                "AABBCCDDEEFF",
                "MONITOR III A.N.C.");

            Assert.Equal("AABBCCDDEEFF", map.BluetoothMac);
            Assert.Equal(containerId, map.ContainerId);
            Assert.Equal("{0.0.0.00000000}.{7a440f74-5176-459d-8615-92f02bddb1b9}", map.RenderEndpointId);
            Assert.Equal("{0.0.1.00000000}.{ca2da733-6c03-4225-8b02-58946b8b8040}", map.CaptureEndpointId);
            Assert.True(map.IsRenderActive);
            Assert.True(map.IsCaptureActive);
        }

        [Fact]
        public async Task ToggleProfile_CyclesBetweenA2dpAndHfp_AndCleansUpSession()
        {
            var fakeMapper = new FakeEndpointMapper();
            FakeHoldSession? lastCreatedSession = null;

            using var controller = new WindowsAudioProfileController(
                fakeMapper,
                (mac, epId) =>
                {
                    lastCreatedSession = new FakeHoldSession(mac, epId, fakeMapper);
                    return lastCreatedSession;
                },
                subscribeSystemEvents: false);

            var device = new BtDeviceItem
            {
                Name = "MONITOR III A.N.C.",
                Mac = "AABBCCDDEEFF",
                Connected = true,
                HfpState = HfpServiceState.Enabled
            };

            // 1. Baseline is A2DP
            var baseline = controller.QueryProfileSnapshot(device);
            Assert.Equal(AudioProfileMode.A2dp, baseline.InferredMode);
            Assert.False(controller.IsHoldingHfpStream);

            // 2. First click -> HFP
            var toHfp = await controller.ToggleProfileAsync(device);
            Assert.True(toHfp.Success);
            Assert.Equal(ProfileTransitionOutcome.SwitchedToHfp, toHfp.Outcome);
            Assert.Equal(AudioProfileMode.HfpHeldByEasyHfp, toHfp.ObservedMode);
            Assert.True(controller.IsHoldingHfpStream);
            Assert.NotNull(lastCreatedSession);
            Assert.True(lastCreatedSession!.IsRunning);

            // 3. Second click -> A2DP
            var toA2dp = await controller.ToggleProfileAsync(device);
            Assert.True(toA2dp.Success);
            Assert.Equal(ProfileTransitionOutcome.SwitchedToA2dp, toA2dp.Outcome);
            Assert.Equal(AudioProfileMode.A2dp, toA2dp.ObservedMode);
            Assert.False(controller.IsHoldingHfpStream);
            Assert.True(lastCreatedSession.Disposed);
        }

        [Fact]
        public async Task SwitchToA2dp_WhenExternalProcessHoldsMic_ReleasesSelfHold_AndNeverClaimsA2dpRestored()
        {
            var fakeMapper = new FakeEndpointMapper();
            FakeHoldSession? createdSession = null;

            using var controller = new WindowsAudioProfileController(
                fakeMapper,
                (mac, epId) =>
                {
                    createdSession = new FakeHoldSession(mac, epId, fakeMapper);
                    return createdSession;
                },
                subscribeSystemEvents: false);

            var device = new BtDeviceItem
            {
                Name = "MONITOR III A.N.C.",
                Mac = "AABBCCDDEEFF",
                Connected = true,
                HfpState = HfpServiceState.Enabled
            };

            // Switch to HFP first
            var hfpRes = await controller.SwitchToHfpAsync(device);
            Assert.True(hfpRes.Success);
            Assert.True(controller.IsHoldingHfpStream);

            // Simulate an external application (PID 29804) also opening the headset microphone
            fakeMapper.ExternalActiveSessions = 1;
            fakeMapper.ExternalPids = new List<uint> { 29804 };

            // Now user clicks Switch to Stereo (A2DP)
            var a2dpRes = await controller.SwitchToA2dpAsync(device);

            // EasyHFP must release its own hold, but MUST NOT claim A2DP was restored!
            Assert.False(controller.IsHoldingHfpStream);
            Assert.True(createdSession!.Disposed);
            Assert.False(a2dpRes.Success);
            Assert.Equal(ProfileTransitionOutcome.ReleasedEasyHfpHoldButExternalAppActive, a2dpRes.Outcome);
            Assert.Equal(AudioProfileMode.HfpHeldByExternalApp, a2dpRes.ObservedMode);
            Assert.NotEqual(AudioProfileMode.A2dp, a2dpRes.ObservedMode);
            Assert.Contains(29804u, a2dpRes.ExternalCaptureProcessIds);
        }

        [Fact]
        public async Task SwitchToHfp_WhenCaptureEndpointMissing_ReportsUnavailableWithoutFallback()
        {
            var fakeMapper = new FakeEndpointMapper
            {
                IsCaptureActive = false,
                CaptureEndpointId = null
            };

            int sessionFactoryCalls = 0;
            using var controller = new WindowsAudioProfileController(
                fakeMapper,
                (mac, epId) =>
                {
                    sessionFactoryCalls++;
                    return new FakeHoldSession(mac, epId, fakeMapper);
                },
                subscribeSystemEvents: false);

            var device = new BtDeviceItem
            {
                Name = "MONITOR III A.N.C.",
                Mac = "AABBCCDDEEFF",
                Connected = true,
                HfpState = HfpServiceState.Disabled
            };

            var res = await controller.SwitchToHfpAsync(device);

            Assert.False(res.Success);
            Assert.Equal(ProfileTransitionOutcome.CaptureEndpointUnavailable, res.Outcome);
            Assert.Equal(0, sessionFactoryCalls);
            Assert.Contains("Hands-Free service", res.UserMessage);
        }

        [Fact]
        public async Task SwitchToHfp_WhenMicrophonePrivacyBlocked_ReturnsMicrophoneAccessDeniedAndDisposesSession()
        {
            var fakeMapper = new FakeEndpointMapper();
            FakeHoldSession? createdSession = null;

            using var controller = new WindowsAudioProfileController(
                fakeMapper,
                (mac, epId) =>
                {
                    createdSession = new FakeHoldSession(mac, epId, fakeMapper)
                    {
                        SimulatedStartHResult = NativeCoreAudio.E_ACCESSDENIED,
                        SimulatedStartSuccess = false
                    };
                    return createdSession;
                },
                subscribeSystemEvents: false);

            var device = new BtDeviceItem
            {
                Name = "MONITOR III A.N.C.",
                Mac = "AABBCCDDEEFF",
                Connected = true,
                HfpState = HfpServiceState.Enabled
            };

            var res = await controller.SwitchToHfpAsync(device);

            Assert.False(res.Success);
            Assert.Equal(ProfileTransitionOutcome.MicrophoneAccessDenied, res.Outcome);
            Assert.False(controller.IsHoldingHfpStream);
            Assert.NotNull(createdSession);
            Assert.True(createdSession!.Disposed);
        }

        [Fact]
        public async Task MidStreamInvalidation_AutomaticallyReleasesHoldAndNotifiesListeners()
        {
            var fakeMapper = new FakeEndpointMapper();
            FakeHoldSession? createdSession = null;

            using var controller = new WindowsAudioProfileController(
                fakeMapper,
                (mac, epId) =>
                {
                    createdSession = new FakeHoldSession(mac, epId, fakeMapper);
                    return createdSession;
                },
                subscribeSystemEvents: false);

            int stateChangedEvents = 0;
            controller.ProfileStateChanged += () => Interlocked.Increment(ref stateChangedEvents);

            var device = new BtDeviceItem
            {
                Name = "MONITOR III A.N.C.",
                Mac = "AABBCCDDEEFF",
                Connected = true,
                HfpState = HfpServiceState.Enabled
            };

            await controller.SwitchToHfpAsync(device);
            Assert.True(controller.IsHoldingHfpStream);

            // Simulate sudden headset disconnect / AUDCLNT_E_DEVICE_INVALIDATED
            createdSession!.SimulateMidStreamInvalidation("AUDCLNT_E_DEVICE_INVALIDATED");

            Assert.False(controller.IsHoldingHfpStream);
            Assert.True(createdSession.Disposed);
            Assert.True(stateChangedEvents >= 2);
        }

        [Fact]
        public async Task Dispose_StopsAndDisposesActiveSessionCleanly()
        {
            var fakeMapper = new FakeEndpointMapper();
            FakeHoldSession? createdSession = null;

            var controller = new WindowsAudioProfileController(
                fakeMapper,
                (mac, epId) =>
                {
                    createdSession = new FakeHoldSession(mac, epId, fakeMapper);
                    return createdSession;
                },
                subscribeSystemEvents: false);

            var device = new BtDeviceItem
            {
                Name = "MONITOR III A.N.C.",
                Mac = "AABBCCDDEEFF",
                Connected = true,
                HfpState = HfpServiceState.Enabled
            };

            await controller.SwitchToHfpAsync(device);
            Assert.True(controller.IsHoldingHfpStream);

            controller.Dispose();

            Assert.False(controller.IsHoldingHfpStream);
            Assert.NotNull(createdSession);
            Assert.True(createdSession!.Disposed);
        }

        private sealed class FakeEndpointMapper : ICoreAudioEndpointMapper
        {
            public string? RenderEndpointId { get; set; } = "{0.0.0.00000000}.{7a440f74-5176-459d-8615-92f02bddb1b9}";
            public string? CaptureEndpointId { get; set; } = "{0.0.1.00000000}.{ca2da733-6c03-4225-8b02-58946b8b8040}";
            public bool IsRenderActive { get; set; } = true;
            public bool IsCaptureActive { get; set; } = true;
            public int SelfActiveSessions { get; set; }
            public int ExternalActiveSessions { get; set; }
            public List<uint> ExternalPids { get; set; } = new();

            public HeadsetAudioEndpointMap ResolveHeadsetEndpoints(string bluetoothMac, string deviceName)
            {
                return new HeadsetAudioEndpointMap
                {
                    BluetoothMac = bluetoothMac,
                    DeviceName = deviceName,
                    ContainerId = new Guid("79875b93-251e-57a6-bc41-5b7a46dfb7c0"),
                    RenderEndpointId = RenderEndpointId,
                    CaptureEndpointId = CaptureEndpointId,
                    IsRenderActive = IsRenderActive,
                    IsCaptureActive = IsCaptureActive,
                    SelfActiveCaptureSessionCount = SelfActiveSessions,
                    ExternalActiveCaptureSessionCount = ExternalActiveSessions,
                    ExternalCaptureProcessIds = new List<uint>(ExternalPids)
                };
            }
        }

        private sealed class FakeHoldSession : IWasapiCaptureHoldSession
        {
            private readonly FakeEndpointMapper _mapper;
            public string TargetMac { get; }
            public string CaptureEndpointId { get; }
            public bool IsRunning { get; private set; }
            public long TotalFramesDiscarded { get; private set; }
            public bool Disposed { get; private set; }

            public bool SimulatedStartSuccess { get; set; } = true;
            public int SimulatedStartHResult { get; set; } = NativeCoreAudio.S_OK;

            public event Action<string>? SessionInvalidated;

            public FakeHoldSession(string targetMac, string captureEndpointId, FakeEndpointMapper mapper)
            {
                TargetMac = targetMac;
                CaptureEndpointId = captureEndpointId;
                _mapper = mapper;
            }

            public Task<WasapiHoldStartResult> StartAsync(CancellationToken cancellationToken = default)
            {
                if (!SimulatedStartSuccess)
                {
                    IsRunning = false;
                    return Task.FromResult(new WasapiHoldStartResult(
                        Started: false,
                        HResult: SimulatedStartHResult,
                        InitialFramesDiscarded: 0,
                        FirstPacketLatencyMs: 0,
                        ErrorMessage: "Simulated failure"));
                }

                IsRunning = true;
                TotalFramesDiscarded = 1600;
                _mapper.SelfActiveSessions = 1;
                return Task.FromResult(new WasapiHoldStartResult(
                    Started: true,
                    HResult: NativeCoreAudio.S_OK,
                    InitialFramesDiscarded: 1600,
                    FirstPacketLatencyMs: 651.2));
            }

            public Task StopAndReleaseAsync()
            {
                IsRunning = false;
                _mapper.SelfActiveSessions = 0;
                return Task.CompletedTask;
            }

            public void SimulateMidStreamInvalidation(string reason)
            {
                IsRunning = false;
                _mapper.SelfActiveSessions = 0;
                SessionInvalidated?.Invoke(reason);
            }

            public void Dispose()
            {
                IsRunning = false;
                _mapper.SelfActiveSessions = 0;
                Disposed = true;
            }
        }
    }
}
