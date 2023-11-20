using System.Diagnostics;
using CloudMesh.NetworkMutex.Abstractions;
using Npgsql;

namespace CloudMesh.NetworkMutex.Postgres;

public class PostgresMutexLock : INetworkMutexLock
{
    public string Id { get; }
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private readonly Stopwatch duration = Stopwatch.StartNew();
    
    public PostgresMutexLock(string id, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        Id = id;
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        this.transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        Activity.Current?.SetTag("network.mutex.lock_id", id);
    }
    
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await transaction.CommitAsync();
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
#if (NET8_0_OR_GREATER)        
        NetworkMutexMetrics.Locks.Add(-1);
#endif
        NetworkMutexMetrics.LockDuration.Record(duration.ElapsedMilliseconds);
    }
}