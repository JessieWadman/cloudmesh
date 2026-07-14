# CloudMesh.NetworkMutex.Abstractions

The shared abstractions for **cluster-wide distributed locks** — a mutual-exclusion primitive that works across
processes and machines, not just threads in one app.

This is the **abstraction-only** package. It defines the contract (`INetworkMutex`, `INetworkMutexLock`) and the
metrics surface, but ships no lock engine. Pick a backend implementation:

- **[CloudMesh.NetworkMutex.Postgres](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.NetworkMutex.Postgres)** — borrows a Postgres transaction + row/table lock. Contenders block until the holder commits (on dispose).
- **[CloudMesh.NetworkMutex.DynamoDB](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.NetworkMutex.DynamoDB)** — borrows DynamoDB optimistic conditional updates + a time-boxed lease. A crashed holder's lock expires automatically.

- **Targets:** .NET 8, 9, 10 — **License:** MIT

---

## Install

You usually don't install this package directly — you install one of the backends, which references it. Install it
on its own only when you need to depend on the interfaces (e.g. a library that takes an `INetworkMutex`):

```bash
dotnet add package CloudMesh.NetworkMutex.Abstractions
```

## The model

A network mutex "borrows" a database engine's own concurrency-control mechanism to enforce exclusivity across the
whole cluster. Anyone contending for the same critical section acquires the **same named lock**; only one holder
anywhere wins at a time.

```csharp
using CloudMesh.NetworkMutex.Abstractions;

public async Task RunExclusiveAsync(INetworkMutex mutex, CancellationToken ct)
{
    // Try to acquire "nightly-report", waiting up to 30 seconds for a contended lock.
    await using var handle = await mutex.TryAcquireLockAsync("nightly-report", TimeSpan.FromSeconds(30));
    if (handle is null)
        return; // another node holds it — skip this run

    // Critical section: guaranteed exclusive across every process/machine sharing the backend.
    await DoWorkAsync(ct);

    // Lock is released when `handle` is disposed at end of scope (await using).
}
```

### Contract semantics

- **Acquire** — `TryAcquireLockAsync(name, ct)` waits until the lock is free or the token is cancelled.
  `TryAcquireLockAsync(name, timeout)` is a convenience overload that cancels after `timeout`.
- **Failure/timeout** — returns `null` (does **not** throw) when the wait is abandoned. Always null-check.
- **Release** — dispose the returned `INetworkMutexLock`. Prefer `await using` so it is released even on exception.
- **Identity** — `INetworkMutexLock.Id` uniquely identifies the acquisition, handy for logging/tracing.

## Metrics

Implementations publish OpenTelemetry-compatible metrics on a single meter. Subscribe by name:

```csharp
using CloudMesh.NetworkMutex.Abstractions;

meterProviderBuilder.AddMeter(NetworkMutexMetrics.MeterName); // "mutex.network"
```

Emitted instruments include lock count, wait time, hold duration, timeouts and errors.

## Use cases

- Leader election / singleton background jobs across a horizontally scaled service.
- Serializing access to an external resource that has no locking of its own.
- Ensuring a scheduled task runs on exactly one node.

## Gotchas

- A returned `null` means "not acquired" — it is normal contention, not an error. Branch on it.
- Never share one `INetworkMutexLock` across the code paths that release it; dispose exactly once.
- Choose a backend deliberately: Postgres blocks and releases on commit/crash; DynamoDB is optimistic and
  lease-based (a crashed holder is freed only after the lease expires). See each implementation's README.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
