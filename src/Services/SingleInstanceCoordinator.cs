using System;
using System.Threading;

namespace EasyHFP.Services
{
    /// <summary>
    /// Coordinates single-instance execution for GUI and System Tray modes,
    /// allowing secondary GUI launches to wake and foreground the existing instance.
    /// </summary>
    public sealed class SingleInstanceCoordinator : IDisposable
    {
        private readonly Mutex _instanceMutex;
        private readonly EventWaitHandle _showWindowEvent;
        private readonly bool _isPrimaryInstance;
        private readonly int _ownerThreadId;
        private RegisteredWaitHandle? _registeredWait;
        private bool _isDisposed;

        public bool IsPrimaryInstance => _isPrimaryInstance;

        public SingleInstanceCoordinator(string? namePrefix = null)
        {
            string resolvedPrefix = !string.IsNullOrWhiteSpace(namePrefix)
                ? namePrefix
                : (Environment.GetEnvironmentVariable("EASYHFP_SINGLE_INSTANCE_PREFIX") ?? @"Local\EasyHFP");

            string mutexName = $"{resolvedPrefix}_SingleInstance_Mutex";
            string eventName = $"{resolvedPrefix}_ShowWindow_Event";

            _instanceMutex = new Mutex(false, mutexName);
            _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;

            try
            {
                _isPrimaryInstance = _instanceMutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                _isPrimaryInstance = true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Single-instance mutex wait failed: {ex.Message}");
                _isPrimaryInstance = true;
            }
        }

        public void RegisterWakeupCallback(Action onShowWindowRequested)
        {
            if (!_isPrimaryInstance || _isDisposed) return;

            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _showWindowEvent,
                (state, timedOut) =>
                {
                    if (!timedOut && !_isDisposed)
                    {
                        try
                        {
                            onShowWindowRequested();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Error executing single-instance wakeup callback.", ex);
                        }
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }

        public void SignalExistingInstanceToShow()
        {
            if (_isDisposed) return;

            try
            {
                _showWindowEvent.Set();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to signal existing instance: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                _registeredWait?.Unregister(null);
                _registeredWait = null;
            }
            catch { }

            if (_isPrimaryInstance && Thread.CurrentThread.ManagedThreadId == _ownerThreadId)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch { }
            }

            try { _instanceMutex.Dispose(); } catch { }
            try { _showWindowEvent.Dispose(); } catch { }
        }
    }
}
