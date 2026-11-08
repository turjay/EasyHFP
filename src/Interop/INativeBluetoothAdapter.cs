using System;

namespace EasyHFP.Interop
{
    public interface INativeBluetoothAdapter
    {
        uint SetServiceState(IntPtr hRadio, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwFlags);
        uint EnumerateInstalledServices(IntPtr hRadio, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi, ref uint pcServices, Guid[]? pGuidServices);
        uint GetDeviceInfo(IntPtr hRadio, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi);
        IntPtr FindFirstDevice(ref NativeBluetooth.BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi);
        bool FindNextDevice(IntPtr hFind, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi);
        bool FindDeviceClose(IntPtr hFind);
    }

    public class Win32BluetoothAdapter : INativeBluetoothAdapter
    {
        public uint SetServiceState(IntPtr hRadio, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwFlags)
            => NativeBluetooth.BluetoothSetServiceState(hRadio, ref pbtdi, ref pGuidService, dwFlags);

        public uint EnumerateInstalledServices(IntPtr hRadio, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi, ref uint pcServices, Guid[]? pGuidServices)
            => NativeBluetooth.BluetoothEnumerateInstalledServices(hRadio, ref pbtdi, ref pcServices, pGuidServices);

        public uint GetDeviceInfo(IntPtr hRadio, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi)
            => NativeBluetooth.BluetoothGetDeviceInfo(hRadio, ref pbtdi);

        public IntPtr FindFirstDevice(ref NativeBluetooth.BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi)
            => NativeBluetooth.BluetoothFindFirstDevice(ref pbtsp, ref pbtdi);

        public bool FindNextDevice(IntPtr hFind, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi)
            => NativeBluetooth.BluetoothFindNextDevice(hFind, ref pbtdi);

        public bool FindDeviceClose(IntPtr hFind)
            => NativeBluetooth.BluetoothFindDeviceClose(hFind);
    }
}
