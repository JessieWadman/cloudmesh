using System.Diagnostics;
using CloudMesh.NetworkMutex.Abstractions;
using Npgsql;

namespace CloudMesh.NetworkMutex.Postgres;

public class PostgresqlMutex : INetworkMutex
{
    private readonly Func<NpgsqlConnection> connectionFactory;

    public PostgresqlMutex(string connectionString)
        : this(() => new NpgsqlConnection(connectionString))
    {
    }

    public PostgresqlMutex(Func<NpgsqlConnection> connectionFactory)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

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
            NetworkMutexMetrics.Locks.Add(1);
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