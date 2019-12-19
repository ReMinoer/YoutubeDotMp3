using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace YoutubeDotMp3.Utils
{
    public class SemaphoreLock : IDisposable
    {
        private readonly SemaphoreSlimQueued _semaphore;
        private readonly Task<IDisposable> _waitTask;
        private bool _accessProvided;
        private bool _disposed;

        public SemaphoreLock(SemaphoreSlimQueued semaphore, CancellationToken cancellationToken)
        {
            _semaphore = semaphore;
            _waitTask = WaitAsync(cancellationToken);

            async Task<IDisposable> WaitAsync(CancellationToken token)
            {
                await _semaphore.WaitAsync(token);
                _accessProvided = true;
                return this;
            }
        }

        [UsedImplicitly]
        public ConfiguredTaskAwaitable<IDisposable>.ConfiguredTaskAwaiter GetAwaiter()
        {
            return _waitTask.ConfigureAwait(false).GetAwaiter();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _waitTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }

            if (_accessProvided)
                _semaphore.Release();
        }
    }
}