using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace EasyHFP.Services
{
    public sealed class ProcessLock : IDisposable
    {
        private const string MutexName = @"Global\EasyHFP_OperationLock";
        private readonly Mutex _mutex;
        private readonly bool _hasHandle;
        private readonly int _ownerThreadId;
        private bool _isDisposed;

        private ProcessLock(Mutex mutex, bool hasHandle)
        {
            _mutex = mutex;
            _hasHandle = hasHandle;
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public bool IsAcquired => _hasHandle;

        public static ProcessLock Acquire(int timeoutMs = 5000)
        {
            Mutex? mutex = null;
            bool createdNew;

            try
            {
                // Set World (Everyone) access so non-elevated and elevated processes share the exact same lock
                var sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                var rule = new MutexAccessRule(sid, MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow);
                var security = new MutexSecurity();
                security.AddAccessRule(rule);

                mutex = MutexAcl.Create(false, MutexName, out createdNew, security);
            }
            catch
            {
                try
                {
                    mutex = Mutex.OpenExisting(MutexName);
                }
                catch
                {
                    // Fall back to standard Mutex if MutexAcl or OpenExisting is unavailable
                    mutex = new Mutex(false, MutexName, out createdNew);
                }
            }

            bool hasHandle = false;
            try
            {
                hasHandle = mutex.WaitOne(timeoutMs, false);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner process crashed or exited without releasing the mutex.
                // We now have acquired ownership safely.
                hasHandle = true;
            }

            return new ProcessLock(mutex, hasHandle);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_hasHandle && Thread.CurrentThread.ManagedThreadId == _ownerThreadId)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
            }

            _mutex.Dispose();
        }
    }
}
