using System;
using System.Runtime.InteropServices;
using System.Threading;
using EasyHFP.Interop;

namespace EasyHFP.Services
{
    public sealed class AudioEndpointWatcher : NativeCoreAudio.IMMNotificationClient, IDisposable
    {
        private readonly AutoResetEvent _changeSignal = new(false);
        private NativeCoreAudio.IMMDeviceEnumerator? _enumerator;
        private bool _isRegistered;

        public event Action? EndpointChanged;

        public AudioEndpointWatcher()
        {
            try
            {
                var comType = typeof(NativeCoreAudio.MMDeviceEnumeratorComObject);
                _enumerator = (NativeCoreAudio.IMMDeviceEnumerator)Activator.CreateInstance(comType)!;
                int hr = _enumerator.RegisterEndpointNotificationCallback(this);
                _isRegistered = (hr == 0);
            }
            catch
            {
                _isRegistered = false;
                _enumerator = null;
            }
        }

        public bool WaitForChange(int timeoutMs)
        {
            return _changeSignal.WaitOne(timeoutMs);
        }

        public void ResetSignal()
        {
            _changeSignal.Reset();
        }

        public void OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState)
        {
            _changeSignal.Set();
            EndpointChanged?.Invoke();
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            _changeSignal.Set();
            EndpointChanged?.Invoke();
        }

        public void OnDeviceRemoved(string pwstrDeviceId)
        {
            _changeSignal.Set();
            EndpointChanged?.Invoke();
        }

        public void OnDefaultDeviceChanged(NativeCoreAudio.EDataFlow flow, NativeCoreAudio.ERole role, string pwstrDefaultDeviceId)
        {
            _changeSignal.Set();
            EndpointChanged?.Invoke();
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, NativeCoreAudio.PROPERTYKEY key)
        {
            _changeSignal.Set();
            EndpointChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_isRegistered && _enumerator != null)
            {
                try
                {
                    _enumerator.UnregisterEndpointNotificationCallback(this);
                }
                catch { }
                _isRegistered = false;
            }

            if (_enumerator != null)
            {
                try
                {
                    Marshal.ReleaseComObject(_enumerator);
                }
                catch { }
                _enumerator = null;
            }

            _changeSignal.Dispose();
        }
    }
}
