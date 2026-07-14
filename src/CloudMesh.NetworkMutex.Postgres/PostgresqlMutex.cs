using System.Diagnostics;
using CloudMesh.NetworkMutex.Abstractions;
using Npgsql;

namespace CloudMesh.NetworkMutex.Postgres;

/// <summary>
/// A Postgres-backed <see cref="INetworkMutex"/>. Exclusivity is enforced by a database transaction that takes
/// a <c>ROW EXCLUSIVE</c> lock on a <c>mutexes</c> table and upserts the named row; a contender's
/// <c>LOCK TABLE</c>/upsert blocks (server-side, up to a one-hour command timeout) until the current holder's
/// transaction commits — which happens when its <see cref="INetworkMutexLock"/> is disposed.
/// </summary>
/// <remarks>
/// <para>
/// Because the lock lives inside a Postgres transaction, it is released deterministically: disposing the handle
/// commits and closes the connection, and if the owning process crashes, Postgres rolls the transaction back and
/// frees the lock automatically. There is no lease to renew.
/// </para>
/// <para>
/// Call <see cref="EnsureTablesExistAsync"/> once during startup to create the backing table before acquiring
/// any locks.
/// </para>
/// </remarks>
public class PostgresqlMutex : INetworkMutex
{
    private readonly Func<NpgsqlConnection> connectionFactory;

    /// <summary>
    /// Creates a mutex that opens a fresh connection per acquisition from the given connection string.
    /// </summary>
    /// <param name="connectionString">An Npgsql connection string pointing at the coordinating database.</param>
    public PostgresqlMutex(string connectionString)
        : this(() => new NpgsqlConnection(connectionString))
    {
    }

    /// <summary>
    /// Creates a mutex that obtains a fresh <see cref="NpgsqlConnection"/> from the supplied factory for each
    /// acquisition. Each held lock owns its own connection for the lifetime of the lock.
    /// </summary>
    /// <param name="connectionFactory">Factory producing an unopened <see cref="NpgsqlConnection"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public PostgresqlMutex(Func<NpgsqlConnection> connectionFactory)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>
    /// Creates the <c>mutexes</c> table that backs the lock if it does not already exist. Safe to call
    /// repeatedly; run it once at startup before acquiring locks.
    /// </summary>
    /// <param name="cancellationToken">Cancels the DDL operation.</param>
    public async Task EnsureTablesExistAsync(CancellationToken cancellationToken)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var trn = await db.BeginTransactionAsync(cancellationToken);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
                               CREATE TABLE IF NOT EXISTS mutexes (
                                   mutex_name varchar PRIMARY KEY,
                                   lock_id bigint
                               );
                          """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await trn.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Opens a connection and transaction, then blocks server-side on a <c>ROW EXCLUSIVE</c> table lock until the
    /// named row is free. On cancellation/timeout the pending connection and transaction are torn down and
    /// <see langword="null"/> is returned; unexpected database errors are rethrown after cleanup.
    /// </remarks>
    public async Task<INetworkMutexLock?> TryAcquireLockAsync(
        string mutexName,
        CancellationToken cancellationToken)
    {
        NpgsqlConnection? connection = null;
        NpgsqlTransaction? transaction = null;
        try
        {
            var duration = Stopwatch.StartNew();
            var lockId = Random.Shared.Next();
            connection = connectionFactory();
            await connection.OpenAsync(cancellationToken);
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 60 * 60; // maximum wait time = 1 hour
            cmd.CommandText = $"""
                                   LOCK TABLE mutexes IN ROW EXCLUSIVE MODE;
                                   INSERT INTO mutexes (mutex_name, lock_id)
                                   VALUES('{mutexName}', {lockId})
                                   ON CONFLICT (mutex_name)
                                   DO
                                     UPDATE SET lock_id = EXCLUDED.lock_id;
                               """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
#if (NET8_0_OR_GREATER)            
            NetworkMutexMetrics.Locks.Add(1);
#endif
            NetworkMutexMetrics.LockWaitTime.Record(duration.ElapsedMilliseconds);
            return new PostgresMutexLock($"{mutexName}#{lockId}", connection, transaction);
        }
        catch (TaskCanceledException)
        {
            NetworkMutexMetrics.Timeouts.Add(1);
            if (transaction != null)
                await transaction.DisposeAsync();
            if (connection != null)
                await connection.DisposeAsync();
            return null;
        }
        catch (OperationCanceledException)
        {
            NetworkMutexMetrics.Timeouts.Add(1);
            if (transaction != null)
                await transaction.DisposeAsync();
            if (connection != null)
                await connection.DisposeAsync();
            return null;
        }
        catch
        {
            NetworkMutexMetrics.Errors.Add(1);
            if (transaction != null)
                await transaction.DisposeAsync();
            if (connection != null)
                await connection.DisposeAsync();
            throw;
        }
    }
}