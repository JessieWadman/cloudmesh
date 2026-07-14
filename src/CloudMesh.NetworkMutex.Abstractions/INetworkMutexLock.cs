namespace CloudMesh.NetworkMutex.Abstractions;

/// <summary>
/// A handle to a held cluster-wide lock obtained from
/// <see cref="INetworkMutex.TryAcquireLockAsync(string, CancellationToken)"/>. The lock is owned for as long as
/// this handle lives; disposing it releases the lock so other contenders can acquire it.
/// </summary>
/// <remarks>
/// Disposal is the release mechanism and is required — leaking the handle keeps the lock held until the backing
/// engine reclaims it (immediately for Postgres, once the connection/transaction is torn down; after the lease
/// expires for DynamoDB). Prefer <c>await using</c> so the lock is released even if the critical section throws.
/// </remarks>
public interface INetworkMutexLock : IAsyncDisposable
{
    /// <summary>
    /// An identifier for this specific acquisition, useful for logging and tracing which holder currently owns
    /// the lock. It is unique to the acquiring attempt, not just to the mutex name.
    /// </summary>
    public string Id { get; }
}
