using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeDotMp3.ViewModels.Utils
{
    public class SemaphoreSlimQueued : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim;
        private readonly ConcurrentQueue<TaskCompletionSource<bool>> _queue = new ConcurrentQueue<TaskCompletionSource<bool>>();

        public int CurrentCount => _semaphoreSlim.CurrentCount;
        public WaitHandle AvailableWaitHandle => _semaphoreSlim.AvailableWaitHandle;

        public SemaphoreSlimQueued(int initialCount)
        {
            _semaphoreSlim = new SemaphoreSlim(initialCount);
        }

        public SemaphoreSlimQueued(int initialCount, int maxCount)
        {
            _semaphoreSlim = new SemaphoreSlim(initialCount, maxCount);
        }

        public Task WaitAsync() => EnqueueAsync(x => x.WaitAsync());
        public Task WaitAsync(CancellationToken cancellationToken) => EnqueueAsync(x => x.WaitAsync(cancellationToken));
        public Task<bool> WaitAsync(TimeSpan timeout) => EnqueueAsync(x => x.WaitAsync(timeout));
        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => EnqueueAsync(x => x.WaitAsync(timeout, cancellationToken));
        public Task<bool> WaitAsync(int millisecondsTimeout) => EnqueueAsync(x => x.WaitAsync(millisecondsTimeout));
        public Task<bool> WaitAsync(int millisecondsTimeout, CancellationToken cancellationToken) => EnqueueAsync(x => x.WaitAsync(millisecondsTimeout, cancellationToken));

        private Task EnqueueAsync(Func<SemaphoreSlim, Task> semaphoreTaskFunc)
        {
            return EnqueueAsync(async x =>
            {
                await semaphoreTaskFunc(x).ConfigureAwait(false);
                return true;
            });
        }

        private async Task<bool> EnqueueAsync(Func<SemaphoreSlim, Task<bool>> semaphoreTaskFunc)
        {
            var queuedTcs = new TaskCompletionSource<bool>();
            _queue.Enqueue(queuedTcs);

            bool result = false;
            try
            {
                result = await semaphoreTaskFunc(_semaphoreSlim).ConfigureAwait(false);
            }
            finally
            {
                _queue.TryDequeue(out TaskCompletionSource<bool> dequeuedTcs);
                dequeuedTcs.SetResult(result);
            }
            
            return await queuedTcs.Task.ConfigureAwait(false);
        }

        public int Release() => _semaphoreSlim.Release();
        public int Release(int releaseCount) => _semaphoreSlim.Release(releaseCount);

        public void Dispose() => _semaphoreSlim.Dispose();
    }
}