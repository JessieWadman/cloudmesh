# CloudMesh.Core

Common, allocation-conscious building blocks for .NET services — async coordination primitives, fast parsing,
buffered IO, and small utility collections. The foundation package much of the rest of CloudMesh builds on.

- **Targets:** .NET 8, 9, 10 — **License:** MIT

```bash
dotnet add package CloudMesh.Core
```

## What's in the box

| Area | Type                                                                  | For |
|---|-----------------------------------------------------------------------|---|
| Async coordination | `AsyncLock`                                                           | An awaitable mutex that's safe to hold across `await` and re-enter from the same async flow. |
| | `AsyncReaderWriterLock`                                               | An awaitable reader/writer lock. |
| | `AsyncLazy<T>`                                                        | `Lazy<T>` with asynchronous initialization. |
| Rate limiting | `Throttler<TKey>`                                                     | Global + per-key concurrency and rate limiting for staying within a dependency's limits. |
| Parsing | `OptimizationHelpers.FastTryParseDecimal`                             | A low-allocation `decimal.TryParse` for hot paths (CSV/numeric feeds), with pluggable `DecimalSeparators`. |
| IO | `BufferedStreamLineReader`,`                                           | Statically-buffered line reader — stream millions of rows with little to no GC. |
| | `SpillStream`                                                         | Seekable read/write stream backed by a bounded paged cache; spills cold pages to a temp file only when needed. |
| Memory | `MemoryHelper`                                                        | Grow/align pooled `Memory<T>` buffers. |
| Collections | `ConcurrentList<T>`, `PriorityQueue<T>`                               | Small concurrent/utility collections. |
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

**`SpillStream`** — a seekable stream that keeps a small working set of pages in pooled memory and spills cold pages
to a temp file *only when it has to*. Small payloads never touch disk; large ones stay bounded to
`pageSize × maxResidentPages` of RAM (default 128 KiB × 16 ≈ 2 MiB) no matter how big the stream grows; and hot
regions (e.g. a columnar file's footer) are served from memory after the first read. Ideal for landing a byte stream
that must then be read back with random access — the middle ground between "spill everything to disk" (pays I/O even
for tiny payloads) and `MemoryStream` (OOMs on huge ones):

```csharp
using CloudMesh.IO;

await using var spill = new SpillStream();   // or new SpillStream(pageSize, maxResidentPages)

await source.CopyToAsync(spill);             // land the bytes (stays in RAM until it outgrows the page budget)
spill.Position = 0;
await ParseWithRandomAccess(spill);          // seek freely; cold pages fault in from a lazily-created temp file

// spill.SpilledToDisk tells you whether a temp file was ever needed.
```

The temp file is created lazily (only on the first dirty-page eviction) with `DeleteOnClose`, and uses
`RandomAccess` positioned I/O, so it's cross-platform and self-cleaning. Not thread-safe — one instance per operation.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
