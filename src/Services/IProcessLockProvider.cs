using System;

namespace EasyHFP.Services
{
    public interface IProcessLockProvider
    {
        IDisposable? TryAcquire(int timeoutMs, out bool acquired);
    }

    public class NamedMutexLockProvider : IProcessLockProvider
    {
        public IDisposable? TryAcquire(int timeoutMs, out bool acquired)
        {
            var lockObj = ProcessLock.Acquire(timeoutMs);
            acquired = lockObj.IsAcquired;
            return lockObj;
        }
    }
}
