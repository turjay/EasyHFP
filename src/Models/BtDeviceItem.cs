using System;
using EasyHFP.Interop;

namespace EasyHFP.Models
{
    public class BtDeviceItem
    {
        public string Name { get; set; } = string.Empty;
        public string Mac { get; set; } = string.Empty;
        public ulong MacUlong { get; set; }
        public bool Connected { get; set; }
        public HfpServiceState HfpState { get; set; } = HfpServiceState.Unknown;
        public AudioProfileMode ActiveProfileMode { get; set; } = AudioProfileMode.Unknown;
        public bool SupportsHfp { get; set; }
        public uint DeviceClass { get; set; }
        public NativeBluetooth.BLUETOOTH_DEVICE_INFO Info;

        public override string ToString()
        {
            return $"{Name} ({(Connected ? "Connected" : "Disconnected")})";
        }
    }
}
