using System.Collections.Concurrent;

namespace CloudMesh.Threading
{
    /// <summary>
    /// The outcome of a call executed through a <see cref="Throttler{TKey}"/>, carrying the result together with
    /// timing information about how long the call waited for the throttle and how long it then ran.
    /// </summary>
    /// <typeparam name="T">The type of value returned by the throttled call.</typeparam>
    public struct ThrottleCallResult<T>
    {
        /// <summary>The value returned by the throttled call.</summary>
        public T Result { get; set; }

        /// <summary>
        /// <see langword="true"/> if the call had to wait before it could run (i.e. it was throttled);
        /// otherwise <see langword="false"/>.
        /// </summary>
        public bool Throttled { get; set; }

        /// <summary>The number of milliseconds the call spent waiting for concurrency/rate slots before it ran.</summary>
        public long ThrottledForMs { get; set; }

        /// <summary>The number of milliseconds the call itself took to complete, once it started running.</summary>
        public long CallDurationMs { get; set; }
    }

    /// <summary>
    /// Limits how often and how concurrently work runs, both globally and per key. Useful for staying within the
    /// rate limits of a downstream dependency (an external API, a database, a partitioned resource) where you want
    /// a global ceiling as well as a per-tenant/per-partition ceiling.
    /// </summary>
    /// <typeparam name="TKey">The key that work is partitioned by (for example a tenant id or endpoint name).</typeparam>
    /// <remarks>
    /// Three independent limits are enforced on every call: a maximum number of calls running at once
    /// (<c>globalConcurrencyLimit</c>), a minimum spacing between calls globally (<c>globalRate</c>), and a minimum
    /// spacing between calls that share the same key (<c>perKeyRate</c>).
    /// </remarks>
    /// <example>
    /// <code>
    /// using var throttler = new Throttler&lt;string&gt;(
    ///     globalConcurrencyLimit: 8,   // at most 8 calls in flight at once
    ///     globalRate: 50,              // at least 50ms between any two calls
    ///     perKeyRate: 200);            // at least 200ms between calls for the same key
    ///
    /// var outcome = await throttler.ExecuteAsync("tenant-a", key =&gt; api.GetAsync(key));
    /// if (outcome.Throttled)
    ///     Console.WriteLine($"Waited {outcome.ThrottledForMs}ms before running.");
    /// </code>
    /// </example>
    public class Throttler<TKey> : IDisposable
        where TKey : notnull
    {
        private readonly SemaphoreSlim globalConcurrencySemaphore;
        private readonly SemaphoreSlim globalRateSemaphore;
        private readonly int globalRate;
        private readonly int perKeyRate;
        private readonly ConcurrentDictionary<TKey, SemaphoreSlim> perKeyRateSemaphores;
        private bool disposed;

        /// <summary>
        /// Creates a new throttler with the given global concurrency, global rate, and per-key rate limits.
        /// </summary>
        /// <param name="globalConcurrencyLimit">The maximum number of calls that may run at the same time across all keys.</param>
        /// <param name="globalRate">The minimum spacing, in milliseconds, enforced between any two calls regardless of key.</param>
        /// <param name="perKeyRate">The minimum spacing, in milliseconds, enforced between two calls that share the same key.</param>
        public Throttler(int globalConcurrencyLimit, int globalRate, int perKeyRate)
        {
            globalConcurrencySemaphore = new SemaphoreSlim(globalConcurrencyLimit,
                 globalConcurrencyLimit);
            globalRateSemaphore = new SemaphoreSlim(1, 1);
            this.globalRate = globalRate;
            this.perKeyRate = perKeyRate;
            perKeyRateSemaphores = new ConcurrentDictionary<TKey, SemaphoreSlim>();
        }

        /// <summary>Releases the semaphores held by the throttler. In-flight calls should be allowed to finish first.</summary>
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

        /// <summary>
        /// Runs <paramref name="taskFactory"/> for the given <paramref name="key"/>, waiting as needed so that the
        /// global concurrency, global rate, and per-key rate limits are respected.
        /// </summary>
        /// <typeparam name="TResult">The type of value produced by the call.</typeparam>
        /// <param name="key">The key the call is partitioned by; drives the per-key rate limit.</param>
        /// <param name="taskFactory">The work to run once a slot is available. Receives the key.</param>
        /// <returns>
        /// A <see cref="ThrottleCallResult{T}"/> containing the result and timing details (whether it was throttled,
        /// for how long, and how long the call itself took).
        /// </returns>
        /// <exception cref="ObjectDisposedException">The throttler has been disposed.</exception>
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
