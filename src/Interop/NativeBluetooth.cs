using System;
using System.Runtime.InteropServices;

namespace EasyHFP.Interop
{
    public static class NativeBluetooth
    {
        public static readonly Guid HfpGuid = new Guid("0000111e-0000-1000-8000-00805f9b34fb");
        public static readonly Guid A2dpSinkGuid = new Guid("0000110b-0000-1000-8000-00805f9b34fb");
        public static readonly Guid HeadsetGuid = new Guid("00001108-0000-1000-8000-00805f9b34fb");
        public static readonly Guid HandsfreeAudioGatewayGuid = new Guid("0000111f-0000-1000-8000-00805f9b34fb");

        [StructLayout(LayoutKind.Sequential)]
        public struct BLUETOOTH_ADDRESS
        {
            public ulong ullLong;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct BLUETOOTH_DEVICE_INFO
        {
            public uint dwSize;
            public BLUETOOTH_ADDRESS Address;
            public uint ulClassofDevice;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fConnected;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fRemembered;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public uint dwSize;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fReturnAuthenticated;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fReturnRemembered;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fReturnUnknown;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fReturnConnected;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        [DllImport("bthprops.cpl")]
        public static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwFlags);

        [DllImport("bthprops.cpl")]
        public static extern uint BluetoothEnumerateInstalledServices(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref uint pcServices, [In, Out] Guid[]? pGuidServices);

        [DllImport("bthprops.cpl")]
        public static extern uint BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);
    }
}
