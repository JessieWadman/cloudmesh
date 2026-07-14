# CloudMesh.NetworkMutex.Postgres

A **Postgres-backed distributed lock** — cluster-wide mutual exclusion that borrows a Postgres transaction and a
row/table lock so only one holder anywhere on the network owns a named lock at a time.

This is a backend for the [CloudMesh.NetworkMutex.Abstractions](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.NetworkMutex.Abstractions)
contract. Prefer this backend when you already run Postgres and want **blocking** acquisition with deterministic,
crash-safe release (no lease to tune). For a DynamoDB backend, see
[CloudMesh.NetworkMutex.DynamoDB](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.NetworkMutex.DynamoDB).

- **Targets:** .NET 8, 9, 10 — **License:** MIT

---

## How it works

Acquiring a lock opens a connection, starts a transaction, takes a `ROW EXCLUSIVE` lock on a `mutexes` table and
upserts the named row. A contender's `LOCK TABLE`/upsert **blocks server-side** (up to a one-hour command timeout)
until the current holder's transaction **commits** — which happens when its lock handle is disposed. If the owning
process crashes, Postgres rolls the transaction back and frees the lock automatically. There is no lease to renew.

## Install

```bash
dotnet add package CloudMesh.NetworkMutex.Postgres
```

## Quick start

```csharp
using CloudMesh.NetworkMutex.Abstractions;
using CloudMesh.NetworkMutex.Postgres;

var mutex = new PostgresqlMutex("Host=db;Username=app;Password=...;Database=app");

// Once at startup: create the backing table if it doesn't exist.
await mutex.EnsureTablesExistAsync(CancellationToken.None);

// Acquire, work, release.
await using var handle = await mutex.TryAcquireLockAsync("nightly-report", TimeSpan.FromSeconds(30));
if (handle is null)
    return; // another node holds the lock — skip

await RunNightlyReportAsync();
// Disposing `handle` (await using) commits the transaction and releases the lock.
```

You can also supply a connection factory instead of a connection string — useful for pooling or per-tenant
databases:

```csharp
var mutex = new PostgresqlMutex(() => new NpgsqlConnection(connectionString));
```

## Use cases

- Leader election / singleton jobs across a scaled-out service that already uses Postgres.
- Serializing a migration or maintenance task to exactly one node.
- Any critical section where you want a contender to **wait** for the lock rather than fail fast.

## Gotchas

- Call `EnsureTablesExistAsync` once before acquiring locks (idempotent).
- A `null` result means the wait was cancelled/timed out — it is normal contention, not an error.
- Each held lock keeps a dedicated connection open for its lifetime; keep critical sections short and always
  `await using` the handle so the connection/transaction are released promptly.
- Acquisition blocks inside Postgres; the command timeout caps the wait at one hour even if your token never fires.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
