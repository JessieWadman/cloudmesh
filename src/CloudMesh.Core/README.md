# CloudMesh.Core

Common, allocation-conscious building blocks for .NET services — async coordination primitives, fast parsing,
buffered IO, and small utility collections. The foundation package much of the rest of CloudMesh builds on.

- **Targets:** .NET 8, 9, 10 — **License:** MIT

```bash
dotnet add package CloudMesh.Core
```

## What's in the box

| Area | Type | For |
|---|---|---|
| Async coordination | `AsyncLock` | An awaitable mutex that's safe to hold across `await` and re-enter from the same async flow. |
| | `AsyncReaderWriterLock` | An awaitable reader/writer lock. |
| | `AsyncLazy<T>` | `Lazy<T>` with asynchronous initialization. |
| Rate limiting | `Throttler<TKey>` | Global + per-key concurrency and rate limiting for staying within a dependency's limits. |
| Parsing | `OptimizationHelpers.FastTryParseDecimal` | A low-allocation `decimal.TryParse` for hot paths (CSV/numeric feeds), with pluggable `DecimalSeparators`. |
| IO | `BufferedStreamLineReader` | Statically-buffered line reader — stream millions of rows with little to no GC. |
| Memory | `MemoryHelper` | Grow/align pooled `Memory<T>` buffers. |
| Collections | `ConcurrentList<T>`, `PriorityQueue<T>` | Small concurrent/utility collections. |
| Helpers | `JsonHelper`, `DateHelper`, `ActivityExtensions`, `HashSetExtensions` | Assorted convenience helpers. |

## Examples

**`AsyncLock`** — a mutex you can `await` and hold across `await` points (a bare `SemaphoreSlim` can't do the
latter safely):

```csharp
using CloudMesh.Utils;

private readonly AsyncLock _gate = new();

public async Task UpdateAsync()
{
    using (await _gate.LockAsync())   // dispose the handle to release
    {
        // critical section — safe to await here, and re-entrant from the same async flow
        await SomethingAsync();
    }
}
```

`AsyncLock` also offers `Lock(...)` (synchronous), and `TryLock`/`TryLockAsync(callback, timeout)` that run a
callback only if the lock is taken in time.

**`Throttler<TKey>`** — enforce a global concurrency ceiling *and* a per-key rate limit at once (e.g. a global
cap plus a per-tenant cap):

```csharp
using CloudMesh.Threading;

using var throttler = new Throttler<string>(
    globalConcurrencyLimit: 8,   // at most 8 calls in flight
    globalRate: 50,              // ≥ 50ms between any two calls
    perKeyRate: 200);           // ≥ 200ms between calls for the same key
```

**`OptimizationHelpers.FastTryParseDecimal`** — a faster, allocation-light `decimal` parser with explicit
separators (`DecimalSeparators.EN_US`, `SV_SE`, `ISO`) so locale-specific numbers parse without a `CultureInfo`.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
