namespace CloudMesh.NetworkMutex.Abstractions;

/// <summary>
/// A cluster-wide (cross-machine) mutual-exclusion primitive. Unlike an in-process lock, a
/// <see cref="INetworkMutex"/> coordinates exclusivity across every process and machine that shares the same
/// backing store, so only one holder anywhere on the network can own a given named lock at a time.
/// </summary>
/// <remarks>
/// <para>
/// The mutex does not implement locking itself — it <em>borrows</em> the concurrency-control mechanism of an
/// external engine. Implementations are provided as separate packages:
/// </para>
/// <list type="bullet">
///   <item><description><c>CloudMesh.NetworkMutex.Postgres</c> — uses a Postgres transaction plus a row/table
///   lock so contenders block until the current holder's transaction commits (on dispose).</description></item>
///   <item><description><c>CloudMesh.NetworkMutex.DynamoDB</c> — uses an optimistic, conditional item update
///   with a time-boxed lease, so a crashed holder's lock expires automatically.</description></item>
/// </list>
/// <para>
/// Acquisition is expressed as a <em>try</em>: it either returns a live <see cref="INetworkMutexLock"/> that you
/// own until you dispose it, or <see langword="null"/> when the attempt is abandoned (for example because the
/// supplied timeout / cancellation elapsed while waiting for a contended lock). Always dispose the returned lock
/// to release it.
/// </para>
/// </remarks>
public interface INetworkMutex
{
    /// <summary>
    /// Attempts to acquire the named cluster-wide lock, waiting until it becomes available or the supplied
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="mutexName">
    /// The logical name of the lock. All processes contending for the same critical section must use the same
    /// name; different names are independent locks.
    /// </param>
    /// <param name="cancellationToken">
    /// Bounds how long the caller is willing to wait for a contended lock. When it fires before the lock is
    /// obtained, the method returns <see langword="null"/> rather than throwing.
    /// </param>
    /// <returns>
    /// An <see cref="INetworkMutexLock"/> representing the acquired lock — dispose it to release — or
    /// <see langword="null"/> if the lock could not be acquired before cancellation/timeout.
    /// </returns>
    Task<INetworkMutexLock?> TryAcquireLockAsync(
        string mutexName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to acquire the named cluster-wide lock, giving up after <paramref name="timeout"/> elapses.
    /// </summary>
    /// <param name="mutexName">The logical name of the lock (see
    /// <see cref="TryAcquireLockAsync(string, CancellationToken)"/>).</param>
    /// <param name="timeout">Maximum time to wait for the lock before returning <see langword="null"/>.</param>
    /// <returns>
    /// An <see cref="INetworkMutexLock"/> to dispose when done, or <see langword="null"/> if the lock was not
    /// acquired within <paramref name="timeout"/>.
    /// </returns>
    /// <example>
    /// <code>
    /// await using var handle = await mutex.TryAcquireLockAsync("nightly-report", TimeSpan.FromSeconds(30));
    /// if (handle is null)
    ///     return; // another node holds the lock; skip this run
    ///
    /// // Critical section — guaranteed exclusive across the cluster.
    /// await RunNightlyReportAsync();
    /// // handle is released when it is disposed at end of scope.
    /// </code>
    /// </example>
    public async Task<INetworkMutexLock?> TryAcquireLockAsync(string mutexName, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await TryAcquireLockAsync(mutexName, cts.Token);
    }
}
