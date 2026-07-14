using CloudMesh.NetworkMutex.Abstractions;
using CloudMesh.NetworkMutex.DynamoDB.Internal;

namespace CloudMesh.NetworkMutex.DynamoDB;

/// <summary>
/// A DynamoDB-backed <see cref="INetworkMutex"/>. Exclusivity is enforced by an <em>optimistic</em>, conditional
/// item update: acquiring the lock writes a time-boxed lease onto the item, and the write only succeeds if the
/// caller already owns the lease or the previous lease has expired. There is no blocking on the server — a
/// contended acquisition fails fast, and the supplied cancellation token bounds any waiting the caller does.
/// </summary>
/// <remarks>
/// <para>
/// Because the lease is time-boxed (see <see cref="MaxLeaseDuration"/>), a crashed holder cannot deadlock the
/// lock: once the lease elapses, the next contender's conditional update succeeds and takes over. Disposing the
/// returned <see cref="INetworkMutexLock"/> releases the lease immediately by zeroing it (guarded by an
/// instance-id condition so it never releases someone else's lease).
/// </para>
/// <para>
/// The backing table must have a string hash key named <c>MutexName</c>. Items also carry an <c>InstanceId</c>
/// and a numeric <c>LeaseUntil</c> (unix milliseconds).
/// </para>
/// </remarks>
public class DynamoDbMutex : INetworkMutex
{
    private readonly string tableName;
    private readonly Func<DateTimeOffset> utcTimeProvider;
    private static readonly string DefaultInstanceId = Guid.NewGuid().ToString();

    /// <summary>
    /// Creates a mutex over the given table, using the system clock (<see cref="DateTimeOffset.UtcNow"/>) for
    /// lease timing.
    /// </summary>
    /// <param name="tableName">Name of the DynamoDB table holding lease items (hash key <c>MutexName</c>).</param>
    public DynamoDbMutex(string tableName)
        : this(tableName, () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>
    /// Creates a mutex over the given table with a custom UTC time source. Useful for deterministic tests.
    /// </summary>
    /// <param name="tableName">Name of the DynamoDB table holding lease items.</param>
    /// <param name="utcTimeProvider">Delegate returning the current UTC time used for lease calculations.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public DynamoDbMutex(string tableName, Func<DateTimeOffset> utcTimeProvider)
    {
        this.tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        this.utcTimeProvider = utcTimeProvider ?? throw new ArgumentNullException(nameof(utcTimeProvider));
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Creates a mutex over the given table using a <see cref="TimeProvider"/> for lease timing.
    /// </summary>
    /// <param name="tableName">Name of the DynamoDB table holding lease items.</param>
    /// <param name="timeProvider">The time provider whose UTC clock drives lease calculations.</param>
    public DynamoDbMutex(string tableName, TimeProvider timeProvider)
        : this(tableName, timeProvider.GetUtcNow)
    {
    }
#endif

    /// <summary>
    /// Maximum time a lease can be held. Lease will automatically time out after this period, even if the
    /// process crashes, or the mutex is not explicitly released. 
    /// </summary>
    public TimeSpan MaxLeaseDuration { get; set; } = TimeSpan.FromHours(1);

    /// <inheritdoc />
    /// <remarks>
    /// Performs an optimistic, conditional lease write. Returns a lock handle when the lease is taken (the item
    /// was free, expired, or already owned by this instance), or <see langword="null"/> when another live lease
    /// holds it. The acquisition does not block server-side.
    /// </remarks>
    public async Task<INetworkMutexLock?> TryAcquireLockAsync(string mutexName, CancellationToken cancellationToken)
    {
        var leaseDto = await DynamoDbHelper.TrySetLeaseAsync(
            tableName,
            mutexName,
            DefaultInstanceId,
            utcTimeProvider,
            MaxLeaseDuration,
            cancellationToken);

        if (leaseDto is not null)
            return new DynamoDbMutexLock(
                tableName, 
                mutexName, 
                leaseDto.InstanceId!);
        return null;
    }
}