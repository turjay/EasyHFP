using System;

namespace EasyHFP.Services
{
    public interface IAudioEndpointMonitor : IDisposable
    {
        bool WaitForChange(int timeoutMs);
        void ResetSignal();
    }

    public class CoreAudioEndpointMonitor : IAudioEndpointMonitor
    {
        private readonly AudioEndpointWatcher _watcher;

        public CoreAudioEndpointMonitor()
        {
            _watcher = new AudioEndpointWatcher();
        }

        public bool WaitForChange(int timeoutMs) => _watcher.WaitForChange(timeoutMs);
        public void ResetSignal() => _watcher.ResetSignal();
        public void Dispose() => _watcher.Dispose();
    }
}
