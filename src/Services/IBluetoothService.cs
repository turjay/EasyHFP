using System;
using System.Collections.Generic;
using EasyHFP.Interop;
using EasyHFP.Models;

namespace EasyHFP.Services
{
    public interface IBluetoothService
    {
        List<BtDeviceItem> EnumerateDevices();
        HfpServiceState QueryHfpState(ref NativeBluetooth.BLUETOOTH_DEVICE_INFO devInfo);
        BtDeviceItem? FindDevice(string identifier, List<BtDeviceItem> devices, out string? ambiguityError);
        TransitionResult ExecuteTransition(BluetoothDeviceSnapshot snapshot);
        bool RestartBluetoothRadio(out string? error);
        bool IsAdministrator();
    }
}
