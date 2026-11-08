using System;
using EasyHFP.Interop;

namespace EasyHFP.Models
{
    public sealed record BluetoothDeviceSnapshot(
        string Mac,
        string Name,
        ulong MacUlong,
        bool Connected,
        TransitionIntent Intent,
        ReconnectMode ReconnectMode,
        NativeBluetooth.BLUETOOTH_DEVICE_INFO DeviceInfo,
        HfpServiceState ObservedHfpState = HfpServiceState.Unknown
    )
    {
        // Target state projected from intent & observed state at snapshot creation time
        public HfpServiceState TargetHfpState => Intent switch
        {
            TransitionIntent.Enable => HfpServiceState.Enabled,
            TransitionIntent.Disable => HfpServiceState.Disabled,
            _ => (ObservedHfpState == HfpServiceState.Enabled ? HfpServiceState.Disabled : HfpServiceState.Enabled)
        };

        public HfpServiceState InitialHfpState => ObservedHfpState;
    }
}
