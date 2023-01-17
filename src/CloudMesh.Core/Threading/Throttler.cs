using System.Collections.Concurrent;

namespace CloudMesh.Threading
{
    public struct ThrottleCallResult<T>
    {
        public T Result { get; set; }
        public bool Throttled { get; set; }
        public long ThrottledForMs { get; set; }
        public long CallDurationMs { get; set; }
    }

    public class Throttler<TKey> : IDisposable
        where TKey : notnull
    {
        private readonly SemaphoreSlim globalConcurrencySemaphore;
        private readonly SemaphoreSlim globalDelaySemaphore;
        private readonly int globalDelay;
        private readonly int perKeyDelay;
        private readonly ConcurrentDictionary<TKey, SemaphoreSlim> perKeyDelaySemaphores;
        private bool disposed;

        public Throttler(int globalConcurrencyLimit, int globalDelay, int perKeyDelay)
        {
            globalConcurrencySemaphore = new SemaphoreSlim(globalConcurrencyLimit,
                 globalConcurrencyLimit);
            globalDelaySemaphore = new SemaphoreSlim(1, 1);
            this.globalDelay = globalDelay;
            this.perKeyDelay = perKeyDelay;
            perKeyDelaySemaphores = new ConcurrentDictionary<TKey, SemaphoreSlim>();
        }

        public void Dispose()
        {
            disposed = true;
            Thread.Sleep(1);
            globalConcurrencySemaphore.Dispose();
            globalDelaySemaphore.Dispose();
            foreach (var ks in perKeyDelaySemaphores.Values)
                ks.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<ThrottleCallResult<TResult>> ExecuteAsync<TResult>(TKey key,
            Func<TKey, Task<TResult>> taskFactory)
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().Name);

            var startWaiting = Environment.TickCount64;

            var perKeyDelaySemaphore = perKeyDelaySemaphores.GetOrAdd(
                key, _ => new SemaphoreSlim(1, 1));
            await perKeyDelaySemaphore.WaitAsync().ConfigureAwait(false);
            ReleaseAsync(perKeyDelaySemaphore, perKeyDelay);
            await globalDelaySemaphore.WaitAsync().ConfigureAwait(false);
            ReleaseAsync(globalDelaySemaphore, globalDelay);
            await globalConcurrencySemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                var waitingCompleted = Environment.TickCount64;

                var task = taskFactory(key);
                var result = await task.ConfigureAwait(false);

                var callCompleted = Environment.TickCount64;

                var msWaiting = waitingCompleted - startWaiting;

                return new ThrottleCallResult<TResult>
                {
                    Result = result,
                    Throttled = msWaiting > 0,
                    ThrottledForMs = msWaiting,
                    CallDurationMs = callCompleted - waitingCompleted
                };
            }
            finally
            {
                globalConcurrencySemaphore.Release();
            }
        }

        private async void ReleaseAsync(SemaphoreSlim semaphore, int delay)
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().Name);

            await Task.Delay(delay).ConfigureAwait(false);
            semaphore.Release();
        }
    }
}
