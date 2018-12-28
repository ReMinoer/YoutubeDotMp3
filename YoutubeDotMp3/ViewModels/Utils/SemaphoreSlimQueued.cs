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

        public Task WaitAsync() => EnqueueAsync(x => x.WaitAsync(), CancellationToken.None);
        public Task WaitAsync(CancellationToken cancellationToken) => EnqueueAsync(x => x.WaitAsync(cancellationToken), cancellationToken);
        public Task<bool> WaitAsync(TimeSpan timeout) => EnqueueAsync(x => x.WaitAsync(timeout), CancellationToken.None);
        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => EnqueueAsync(x => x.WaitAsync(timeout, cancellationToken), cancellationToken);
        public Task<bool> WaitAsync(int millisecondsTimeout) => EnqueueAsync(x => x.WaitAsync(millisecondsTimeout), CancellationToken.None);
        public Task<bool> WaitAsync(int millisecondsTimeout, CancellationToken cancellationToken) => EnqueueAsync(x => x.WaitAsync(millisecondsTimeout, cancellationToken), cancellationToken);

        private Task EnqueueAsync(Func<SemaphoreSlim, Task> semaphoreTaskFunc, CancellationToken cancellationToken)
        {
            return EnqueueAsync(async x =>
            {
                await semaphoreTaskFunc(x);
                return true;
            }, cancellationToken);
        }

        private Task<bool> EnqueueAsync(Func<SemaphoreSlim, Task<bool>> semaphoreTaskFunc, CancellationToken cancellationToken)
        {
            var queuedTcs = new TaskCompletionSource<bool>();
            _queue.Enqueue(queuedTcs);

            semaphoreTaskFunc(_semaphoreSlim).ContinueWith(t =>
            {
                if (_queue.TryDequeue(out TaskCompletionSource<bool> dequeuedTcs))
                    dequeuedTcs.SetResult(t.Result);
            }, cancellationToken, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());

            return queuedTcs.Task;
        }

        public int Release() => _semaphoreSlim.Release();
        public int Release(int releaseCount) => _semaphoreSlim.Release(releaseCount);

        public void Dispose() => _semaphoreSlim.Dispose();
    }
}