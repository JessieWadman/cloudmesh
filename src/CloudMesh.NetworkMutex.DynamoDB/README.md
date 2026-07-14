# CloudMesh.NetworkMutex.DynamoDB

A **DynamoDB-backed distributed lock** — cluster-wide mutual exclusion that borrows DynamoDB's optimistic,
conditional item updates plus a **time-boxed lease**, so a crashed holder's lock frees itself automatically.

This is a backend for the [CloudMesh.NetworkMutex.Abstractions](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.NetworkMutex.Abstractions)
contract. Prefer this backend on AWS / serverless where you don't want to run a relational database. For a
blocking, transaction-based backend, see
[CloudMesh.NetworkMutex.Postgres](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.NetworkMutex.Postgres).

- **Targets:** .NET 8, 9, 10 — **License:** MIT

---

## How it works

Acquiring a lock performs an **optimistic conditional update** on a lease item: it stamps the item with this
instance's id and a `LeaseUntil` expiry, but only if the item is free, already owned by this instance, or the
previous lease has **expired**. There is no server-side blocking — a contended acquisition fails fast, and the
cancellation token you pass bounds any waiting your caller does.

Because the lease is time-boxed (`MaxLeaseDuration`, default 1 hour), a crashed holder can never deadlock the
lock: once the lease elapses, the next contender's conditional update takes over. Disposing the handle releases
the lease immediately (guarded so it never clobbers a lease someone else already took over).

### Table requirements

The backing table needs a **string hash key named `MutexName`**. Items also carry `InstanceId` and a numeric
`LeaseUntil` (unix milliseconds).

## Install

```bash
dotnet add package CloudMesh.NetworkMutex.DynamoDB
```

## Quick start

```csharp
using CloudMesh.NetworkMutex.Abstractions;
using CloudMesh.NetworkMutex.DynamoDB;

var mutex = new DynamoDbMutex("app-mutexes")
{
    MaxLeaseDuration = TimeSpan.FromMinutes(5) // safety cap if a holder crashes
};

await using var handle = await mutex.TryAcquireLockAsync("nightly-report", TimeSpan.FromSeconds(30));
if (handle is null)
    return; // another live lease holds it — skip

await RunNightlyReportAsync();
// Disposing `handle` (await using) releases the lease immediately.
```

For deterministic tests, inject a clock via `Func<DateTimeOffset>` or a `TimeProvider`:

```csharp
var mutex = new DynamoDbMutex("app-mutexes", timeProvider);
```

## Use cases

- Leader election / singleton jobs on AWS without a relational database.
- Self-healing locks where a crashed node must not hold a lock forever (the lease expires).
- Fail-fast coordination where you'd rather skip work than block waiting.

## Gotchas

- Set `MaxLeaseDuration` to comfortably exceed your critical section's worst case — if the lease expires while you
  still hold it, another node can acquire it.
- A `null` result means a live lease already holds the lock — normal contention, not an error.
- Acquisition is optimistic (no server-side wait); the timeout/token governs only your own retry/wait window.
- Uses the default AWS credential/region resolution of `AmazonDynamoDBClient`.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
