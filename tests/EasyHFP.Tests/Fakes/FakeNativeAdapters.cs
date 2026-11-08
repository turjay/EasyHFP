using System;
using System.Collections.Generic;
using EasyHFP.Interop;
using EasyHFP.Models;
using EasyHFP.Services;

namespace EasyHFP.Tests.Fakes
{
    public class FakeNativeBluetoothAdapter : INativeBluetoothAdapter
    {
        public bool QueryError { get; set; } = false;
        public uint SetServiceStateResult { get; set; } = 0; // 0 = ERROR_SUCCESS
        public bool IsHfpInstalled { get; set; } = false;
        public int SetServiceStateCallCount { get; private set; } = 0;
        public uint LastSetFlags { get; private set; } = 0;

        public uint SetServiceState(IntPtr hRadio, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwFlags)
        {
            SetServiceStateCallCount++;
            LastSetFlags = dwFlags;

            if (SetServiceStateResult == 0)
            {
                // In hardware, a successful call modifies the installed service registry
                IsHfpInstalled = (dwFlags == 1);
            }

            return SetServiceStateResult;
        }

        public uint EnumerateInstalledServices(IntPtr hRadio, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi, ref uint pcServices, Guid[]? pGuidServices)
        {
            if (QueryError)
            {
                return 14; // ERROR_OUTOFMEMORY or generic failure
            }

            if (pGuidServices == null)
            {
                pcServices = IsHfpInstalled ? 1u : 0u;
                return IsHfpInstalled ? 234u : 0u; // 234 = ERROR_MORE_DATA
            }

            if (IsHfpInstalled)
            {
                pcServices = 1;
                pGuidServices[0] = NativeBluetooth.HfpGuid;
            }
            else
            {
                pcServices = 0;
            }

            return 0; // ERROR_SUCCESS
        }

        public uint GetDeviceInfo(IntPtr hRadio, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi) => 0;
        public IntPtr FindFirstDevice(ref NativeBluetooth.BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi) => IntPtr.Zero;
        public bool FindNextDevice(IntPtr hFind, ref NativeBluetooth.BLUETOOTH_DEVICE_INFO pbtdi) => false;
        public bool FindDeviceClose(IntPtr hFind) => true;
    }

    public class FakeNativePnpAdapter : INativePnpAdapter
    {
        public bool RestartSucceeds { get; set; } = true;
        public int RestartCallCount { get; private set; } = 0;
        public string? LastRestartedMac { get; private set; }

        public NativePnp.PnpRestartResult RestartDeviceNodes(string macHex, int waitMs = 800)
        {
            RestartCallCount++;
            LastRestartedMac = macHex;

            return new NativePnp.PnpRestartResult
            {
                Success = RestartSucceeds,
                NodesFound = 2,
                NodesDisabled = 2,
                NodesEnabled = RestartSucceeds ? 2 : 0,
                ErrorMessage = RestartSucceeds ? null : "Fake PnP node restart failure"
            };
        }
    }

    public class FakeRadioController : IRadioController
    {
        public bool RestartSucceeds { get; set; } = true;
        public int RestartCallCount { get; private set; } = 0;

        public bool RestartBluetoothRadio(out string? error)
        {
            RestartCallCount++;
            if (RestartSucceeds)
            {
                error = null;
                return true;
            }
            error = "Fake radio restart failure";
            return false;
        }
    }

    public class FakeAudioEndpointMonitor : IAudioEndpointMonitor
    {
        public bool HasPendingNotification { get; set; } = false;

        public bool WaitForChange(int timeoutMs)
        {
            return HasPendingNotification;
        }

        public void ResetSignal()
        {
            HasPendingNotification = false;
        }

        public void Dispose() { }
    }

    public class FakeProcessLockProvider : IProcessLockProvider
    {
        public bool AllowAcquire { get; set; } = true;
        public int AcquireCallCount { get; private set; } = 0;

        public IDisposable? TryAcquire(int timeoutMs, out bool acquired)
        {
            AcquireCallCount++;
            acquired = AllowAcquire;
            return AllowAcquire ? new DummyDisposable() : null;
        }

        private class DummyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
