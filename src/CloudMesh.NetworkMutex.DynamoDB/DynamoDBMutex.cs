using CloudMesh.NetworkMutex.Abstractions;
using CloudMesh.NetworkMutex.DynamoDB.Internal;

namespace CloudMesh.NetworkMutex.DynamoDB;

public class DynamoDbMutex : INetworkMutex
{
    private readonly string tableName;
    private readonly Func<DateTimeOffset> utcTimeProvider;
    private static readonly string DefaultInstanceId = Guid.NewGuid().ToString();

    public DynamoDbMutex(string tableName)
        : this(tableName, () => DateTimeOffset.UtcNow)
    {
    }
    
    public DynamoDbMutex(string tableName, Func<DateTimeOffset> utcTimeProvider)
    {
        this.tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        this.utcTimeProvider = utcTimeProvider ?? throw new ArgumentNullException(nameof(utcTimeProvider));
    }
    
#if NET8_0_OR_GREATER
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