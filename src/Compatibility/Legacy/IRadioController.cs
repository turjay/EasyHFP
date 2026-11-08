using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.Devices.Radios;

namespace EasyHFP.Services
{
    public interface IRadioController
    {
        bool RestartBluetoothRadio(out string? error);
    }

    public class WinRtRadioController : IRadioController
    {
        public bool RestartBluetoothRadio(out string? error)
        {
            error = null;
            try
            {
                var radiosOp = Radio.GetRadiosAsync();
                var radiosTask = System.WindowsRuntimeSystemExtensions.AsTask<IReadOnlyList<Radio>>(radiosOp);
                radiosTask.Wait(5000);
                var radios = radiosTask.Result;
                var btRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

                if (btRadio == null)
                {
                    error = "No Bluetooth radio found on this system.";
                    return false;
                }

                var offOp = btRadio.SetStateAsync(RadioState.Off);
                var offTask = System.WindowsRuntimeSystemExtensions.AsTask<RadioAccessStatus>(offOp);
                offTask.Wait(4000);
                if (offTask.Result != RadioAccessStatus.Allowed)
                {
                    error = $"Radio off request was not allowed: {offTask.Result}";
                    return false;
                }

                Thread.Sleep(1500);

                var onOp = btRadio.SetStateAsync(RadioState.On);
                var onTask = System.WindowsRuntimeSystemExtensions.AsTask<RadioAccessStatus>(onOp);
                onTask.Wait(4000);
                if (onTask.Result != RadioAccessStatus.Allowed)
                {
                    error = $"Radio on request was not allowed: {onTask.Result}";
                    return false;
                }

                Thread.Sleep(1000);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Exception cycling Bluetooth radio: {ex.Message}";
                return false;
            }
        }
    }
}
