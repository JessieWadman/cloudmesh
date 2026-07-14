using System.Diagnostics;
using CloudMesh.NetworkMutex.Abstractions;
using Npgsql;

namespace CloudMesh.NetworkMutex.Postgres;

/// <summary>
/// A held Postgres lock. Owns the open connection and the transaction whose row/table lock provides exclusivity.
/// Disposing this handle commits the transaction and closes the connection, releasing the lock for other
/// contenders.
/// </summary>
public class PostgresMutexLock : INetworkMutexLock
{
    /// <inheritdoc />
    public string Id { get; }
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private readonly Stopwatch duration = Stopwatch.StartNew();

    /// <summary>
    /// Creates a handle over an already-acquired lock. Intended to be constructed by
    /// <see cref="PostgresqlMutex"/>; the handle takes ownership of the connection and transaction.
    /// </summary>
    /// <param name="id">A unique identifier for this acquisition (surfaced as <see cref="Id"/>).</param>
    /// <param name="connection">The open connection backing the held lock.</param>
    /// <param name="transaction">The transaction whose lock provides exclusivity; committed on dispose.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="transaction"/> is <see langword="null"/>.</exception>
    public PostgresMutexLock(string id, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        Id = id;
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        this.transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        Activity.Current?.SetTag("network.mutex.lock_id", id);
    }
    
    /// <summary>
    /// Releases the lock by committing the transaction and disposing the transaction and connection. Records
    /// lock-duration and lock-count metrics.
    /// </summary>
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