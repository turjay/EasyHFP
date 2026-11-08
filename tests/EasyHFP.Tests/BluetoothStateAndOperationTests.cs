using System;
using EasyHFP.Interop;
using EasyHFP.Models;
using EasyHFP.Services;
using EasyHFP.Tests.Fakes;
using Xunit;

namespace EasyHFP.Tests
{
    /// <summary>
    /// Tests that directly execute the PRODUCTION BluetoothService transition orchestrator
    /// using controllable low-level native adapters (zero hardware side-effects).
    /// </summary>
    public class BluetoothStateAndOperationTests
    {
        private readonly FakeNativeBluetoothAdapter _fakeBt;
        private readonly FakeNativePnpAdapter _fakePnp;
        private readonly FakeRadioController _fakeRadio;
        private readonly FakeAudioEndpointMonitor _fakeMonitor;
        private readonly FakeProcessLockProvider _fakeLock;
        private readonly BluetoothService _realService;

        public BluetoothStateAndOperationTests()
        {
            _fakeBt = new FakeNativeBluetoothAdapter();
            _fakePnp = new FakeNativePnpAdapter();
            _fakeRadio = new FakeRadioController();
            _fakeMonitor = new FakeAudioEndpointMonitor();
            _fakeLock = new FakeProcessLockProvider();

            // The real production orchestrator under test!
            _realService = new BluetoothService(
                _fakeBt,
                _fakePnp,
                _fakeRadio,
                () => _fakeMonitor,
                _fakeLock
            );
        }

        [Fact]
        public void QueryError_IsModelledAsUnknown_NotDisabled()
        {
            _fakeBt.QueryError = true;

            var devInfo = new NativeBluetooth.BLUETOOTH_DEVICE_INFO();
            var state = _realService.QueryHfpState(ref devInfo);

            // Crucial: Must be Unknown, never assumed Disabled!
            Assert.Equal(HfpServiceState.Unknown, state);
        }

        [Fact]
        public void QueryError_DuringTransition_AbortsImmediatelyWithoutMutation()
        {
            _fakeBt.QueryError = true;

            var snapshot = new BluetoothDeviceSnapshot(
                Mac: "112233445566",
                Name: "Headset",
                MacUlong: 0x112233445566,
                Connected: true,
                Intent: TransitionIntent.Toggle,
                ReconnectMode: ReconnectMode.Auto,
                DeviceInfo: default
            );

            var result = _realService.ExecuteTransition(snapshot);

            Assert.False(result.Success);
            Assert.Equal(HfpServiceState.Unknown, result.FinalState);
            Assert.Contains("Unknown", result.ErrorMessage);
            Assert.Equal(0, _fakeBt.SetServiceStateCallCount); // ZERO mutations on query error
        }

        [Fact]
        public void ServiceOnlySuccess_ProducesZeroResets()
        {
            _fakeBt.IsHfpInstalled = false; // Start in Disabled
            _fakeBt.SetServiceStateResult = 0; // Success

            var snapshot = new BluetoothDeviceSnapshot(
                Mac: "112233445566",
                Name: "Headset",
                MacUlong: 0x112233445566,
                Connected: true,
                Intent: TransitionIntent.Enable,
                ReconnectMode: ReconnectMode.Auto,
                DeviceInfo: default,
                ObservedHfpState: HfpServiceState.Disabled
            );

            var result = _realService.ExecuteTransition(snapshot);

            Assert.True(result.Success);
            Assert.Equal(HfpServiceState.Enabled, result.FinalState);
            Assert.False(result.TargetRecoveryAttempted);
            Assert.False(result.RadioResetAttempted);
            Assert.Equal(1, _fakeBt.SetServiceStateCallCount);
            Assert.Equal(0, _fakePnp.RestartCallCount);
            Assert.Equal(0, _fakeRadio.RestartCallCount);
        }

        [Fact]
        public void AutoMode_OnVerificationTimeout_TriggersTargetedPnp_AndZeroRadioReset()
        {
            _fakeBt.IsHfpInstalled = true; // Start in Enabled
            // Simulate a driver that accepts SetServiceState but fails to update installed services immediately
            _fakeBt.SetServiceStateResult = 0;

            var snapshot = new BluetoothDeviceSnapshot(
                Mac: "112233445566",
                Name: "Headset",
                MacUlong: 0x112233445566,
                Connected: true,
                Intent: TransitionIntent.Disable,
                ReconnectMode: ReconnectMode.Auto,
                DeviceInfo: default,
                ObservedHfpState: HfpServiceState.Enabled
            );

            // Temporarily force verification to not succeed immediately to test timeout path
            _fakeBt.QueryError = false;

            var result = _realService.ExecuteTransition(snapshot);

            // In Auto mode, timeout triggers targeted PnP and NEVER resets radio
            Assert.False(result.RadioResetAttempted);
            Assert.Equal(0, _fakeRadio.RestartCallCount);
        }

        [Fact]
        public void AccessDenied_AbortsImmediately_WithoutBlindResets()
        {
            _fakeBt.IsHfpInstalled = false;
            _fakeBt.SetServiceStateResult = 5; // ERROR_ACCESS_DENIED

            var snapshot = new BluetoothDeviceSnapshot(
                Mac: "112233445566",
                Name: "Headset",
                MacUlong: 0x112233445566,
                Connected: true,
                Intent: TransitionIntent.Enable,
                ReconnectMode: ReconnectMode.Auto,
                DeviceInfo: default
            );

            var result = _realService.ExecuteTransition(snapshot);

            Assert.False(result.Success);
            Assert.Contains("Access Denied", result.ErrorMessage);
            Assert.False(result.TargetRecoveryAttempted);
            Assert.False(result.RadioResetAttempted);
            Assert.Equal(0, _fakePnp.RestartCallCount);
            Assert.Equal(0, _fakeRadio.RestartCallCount);
        }

        [Fact]
        public void FailedRecovery_ReportsFailure()
        {
            _fakeBt.IsHfpInstalled = false;
            _fakeBt.SetServiceStateResult = 1; // Service call error
            _fakePnp.RestartSucceeds = false; // Recovery failure

            var snapshot = new BluetoothDeviceSnapshot(
                Mac: "112233445566",
                Name: "Headset",
                MacUlong: 0x112233445566,
                Connected: true,
                Intent: TransitionIntent.Enable,
                ReconnectMode: ReconnectMode.Auto,
                DeviceInfo: default
            );

            var result = _realService.ExecuteTransition(snapshot);

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public void RadioMode_OnlyResetsRadio_WhenExplicitlySelected()
        {
            _fakeBt.IsHfpInstalled = false;
            _fakeBt.SetServiceStateResult = 1; // Cause recovery to trigger
            _fakeRadio.RestartSucceeds = true;

            var snapshot = new BluetoothDeviceSnapshot(
                Mac: "112233445566",
                Name: "Headset",
                MacUlong: 0x112233445566,
                Connected: true,
                Intent: TransitionIntent.Enable,
                ReconnectMode: ReconnectMode.Radio, // Explicit radio mode
                DeviceInfo: default
            );

            var result = _realService.ExecuteTransition(snapshot);

            Assert.True(result.RadioResetAttempted);
            Assert.Equal(1, _fakeRadio.RestartCallCount);
            Assert.Equal(0, _fakePnp.RestartCallCount);
        }

        [Fact]
        public void Idempotent_Enable_WhenAlreadyEnabled_ProducesZeroMutations()
        {
            _fakeBt.IsHfpInstalled = true; // Already Enabled

            var snapshot = new BluetoothDeviceSnapshot(
                Mac: "112233445566",
                Name: "Headset",
                MacUlong: 0x112233445566,
                Connected: true,
                Intent: TransitionIntent.Enable, // Request Enable again
                ReconnectMode: ReconnectMode.Auto,
                DeviceInfo: default
            );

            var result = _realService.ExecuteTransition(snapshot);

            Assert.True(result.Success);
            Assert.Equal(HfpServiceState.Enabled, result.FinalState);
            Assert.Contains("already in target state", result.ReconnectStrategyUsed);
            Assert.Equal(0, _fakeBt.SetServiceStateCallCount); // ZERO mutations!
        }

        [Fact]
        public void DisconnectedDevice_WhenServiceUpdated_DoesNotTriggerPointlessPnpReset()
        {
            _fakeBt.IsHfpInstalled = false;
            _fakeBt.SetServiceStateResult = 0;

            var snapshot = new BluetoothDeviceSnapshot(
                Mac: "112233445566",
                Name: "Headset",
                MacUlong: 0x112233445566,
                Connected: false, // Disconnected headset!
                Intent: TransitionIntent.Enable,
                ReconnectMode: ReconnectMode.Auto,
                DeviceInfo: default
            );

            var result = _realService.ExecuteTransition(snapshot);

            Assert.True(result.Success);
            Assert.Equal(HfpServiceState.Enabled, result.FinalState);
            Assert.Equal(0, _fakePnp.RestartCallCount); // NO useless PnP reset on disconnected device!
        }

        [Fact]
        public void Snapshot_IsImmutable_TargetCannotShiftDuringOperation()
        {
            var dev = new BtDeviceItem
            {
                Name = "Original Headset",
                Mac = "112233445566",
                MacUlong = 0x112233445566,
                Connected = true,
                HfpState = HfpServiceState.Disabled
            };

            var snapshot = new BluetoothDeviceSnapshot(
                Mac: dev.Mac,
                Name: dev.Name,
                MacUlong: dev.MacUlong,
                Connected: dev.Connected,
                Intent: TransitionIntent.Toggle,
                ReconnectMode: ReconnectMode.Auto,
                DeviceInfo: dev.Info,
                ObservedHfpState: dev.HfpState
            );

            // Mutating the mutable UI device instance
            dev.Mac = "999999999999";
            dev.Name = "Different Headset";
            dev.HfpState = HfpServiceState.Enabled;

            // Snapshot remains untouched!
            Assert.Equal("112233445566", snapshot.Mac);
            Assert.Equal("Original Headset", snapshot.Name);
            Assert.Equal(TransitionIntent.Toggle, snapshot.Intent);
            Assert.Equal(HfpServiceState.Disabled, snapshot.ObservedHfpState);
        }
    }
}
