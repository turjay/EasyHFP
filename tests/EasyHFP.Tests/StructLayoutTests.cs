using System;
using System.Runtime.InteropServices;
using EasyHFP.Interop;
using Xunit;

namespace EasyHFP.Tests
{
    public class StructLayoutTests
    {
        [Fact]
        public void BluetoothDeviceInfo_HasExpectedSizeAndLayout()
        {
            int size = Marshal.SizeOf<NativeBluetooth.BLUETOOTH_DEVICE_INFO>();

            // On 64-bit Windows:
            // dwSize (4) + pad (4) + Address (8) + ulClassofDevice (4) + fConnected (4)
            // + fRemembered (4) + fAuthenticated (4) + stLastSeen (16) + stLastUsed (16)
            // + szName (248 * 2 = 496) = 560 bytes
            if (IntPtr.Size == 8)
            {
                Assert.Equal(560, size);
            }
            else
            {
                Assert.Equal(556, size);
            }
        }

        [Fact]
        public void BluetoothDeviceSearchParams_HasExpectedSize()
        {
            int size = Marshal.SizeOf<NativeBluetooth.BLUETOOTH_DEVICE_SEARCH_PARAMS>();

            // dwSize (4) + 5 x BOOL (20) + cTimeoutMultiplier (1) + padding + hRadio (IntPtr)
            if (IntPtr.Size == 8)
            {
                Assert.Equal(40, size);
            }
            else
            {
                Assert.Equal(32, size);
            }
        }

        [Fact]
        public void SpDevInfoData_HasExpectedSize()
        {
            int size = Marshal.SizeOf<NativePnp.SP_DEVINFO_DATA>();

            // cbSize (4) + ClassGuid (16) + DevInst (4) + padding + Reserved (IntPtr)
            if (IntPtr.Size == 8)
            {
                Assert.Equal(32, size);
            }
            else
            {
                Assert.Equal(28, size);
            }
        }
    }
}
