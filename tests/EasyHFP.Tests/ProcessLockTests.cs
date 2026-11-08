using System;
using System.Threading;
using System.Threading.Tasks;
using EasyHFP.Services;
using Xunit;

namespace EasyHFP.Tests
{
    public class ProcessLockTests
    {
        [Fact]
        public void ProcessLock_AcquireAndDispose_ReleasesCleanly()
        {
            using (var lockObj = ProcessLock.Acquire(timeoutMs: 2000))
            {
                Assert.True(lockObj.IsAcquired);
            }

            // Immediately acquiring again should succeed because it was cleanly disposed
            using (var lockObj2 = ProcessLock.Acquire(timeoutMs: 2000))
            {
                Assert.True(lockObj2.IsAcquired);
            }
        }

        [Fact]
        public async Task ProcessLock_ConcurrentAcquisition_BlocksUntilReleased()
        {
            var holdLockEvent = new ManualResetEventSlim(false);
            var lockAcquiredEvent = new ManualResetEventSlim(false);

            // Task 1 acquires the lock and holds it
            var holderTask = Task.Run(() =>
            {
                using (var l = ProcessLock.Acquire(timeoutMs: 3000))
                {
                    Assert.True(l.IsAcquired);
                    lockAcquiredEvent.Set();
                    holdLockEvent.Wait(4000);
                }
            });

            lockAcquiredEvent.Wait(2000);

            // Task 2 attempts to acquire while Task 1 is holding: must time out!
            using (var blockedLock = ProcessLock.Acquire(timeoutMs: 150))
            {
                Assert.False(blockedLock.IsAcquired);
            }

            // Release Task 1
            holdLockEvent.Set();
            await holderTask;

            // Now Task 2 can acquire successfully
            using (var secondLock = ProcessLock.Acquire(timeoutMs: 3000))
            {
                Assert.True(secondLock.IsAcquired);
            }
        }
    }
}
