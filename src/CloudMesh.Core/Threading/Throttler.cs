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
        private readonly SemaphoreSlim globalRateSemaphore;
        private readonly int globalRate;
        private readonly int perKeyRate;
        private readonly ConcurrentDictionary<TKey, SemaphoreSlim> perKeyRateSemaphores;
        private bool disposed;

        public Throttler(int globalConcurrencyLimit, int globalRate, int perKeyRate)
        {
            globalConcurrencySemaphore = new SemaphoreSlim(globalConcurrencyLimit,
                 globalConcurrencyLimit);
            globalRateSemaphore = new SemaphoreSlim(1, 1);
            this.globalRate = globalRate;
            this.perKeyRate = perKeyRate;
            perKeyRateSemaphores = new ConcurrentDictionary<TKey, SemaphoreSlim>();
        }

        public void Dispose()
        {
            disposed = true;
            Thread.Sleep(1);
            globalConcurrencySemaphore.Dispose();
            globalRateSemaphore.Dispose();
            foreach (var ks in perKeyRateSemaphores.Values)
                ks.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<ThrottleCallResult<TResult>> ExecuteAsync<TResult>(TKey key,
            Func<TKey, Task<TResult>> taskFactory)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var startWaiting = Environment.TickCount64;

            var perKeyRateSemaphore = perKeyRateSemaphores.GetOrAdd(
                key, _ => new SemaphoreSlim(1, 1));
            await perKeyRateSemaphore.WaitAsync().ConfigureAwait(false);
            ReleaseAsync(perKeyRateSemaphore, perKeyRate);
            await globalRateSemaphore.WaitAsync().ConfigureAwait(false);
            ReleaseAsync(globalRateSemaphore, globalRate);
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
