# CloudMesh.Uuid

The fastest way to mint **time-sortable UUID v7** values on .NET — a drop-in `Guid` that's cheaper than
`Guid.NewGuid()` and orders roughly by creation time.

UUID v7 (RFC 9562) stores a millisecond timestamp in its high bits, so ids generated over time sort — as bytes
*and* as text — in approximately chronological order. That gives you database index locality close to a
sequential key while staying 128-bit globally unique. This package returns a plain `System.Guid`, so it slots
straight into EF Core keys, `Guid` columns, and any API that already expects one.

- **Targets:** .NET 8, 9, 10 — **License:** MIT
- **Allocation-free** and about **1.5× faster than `Guid.NewGuid()`**, ~2.5× faster than `Guid.CreateVersion7()`.
- Timestamp comes from [`FastClock`](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.Timestamp),
  so the embedded time stays accurate (NTP/VM-resume aware) without paying for `DateTimeOffset.UtcNow` per id.

---

## Install

```bash
dotnet add package CloudMesh.Uuid
```

## Quick start

```csharp
// The common path: a v7 UUID stamped with "now".
Guid id = Uuid.Create();

// Back-fill an id for a record with a known creation time:
Guid backfilled = Uuid.Next(order.CreatedAt);           // DateTimeOffset overload
Guid fromUnix   = Uuid.Next(1_726_000_000_000L);        // Unix-milliseconds overload

// Deterministic timestamps in tests via a fake TimeProvider:
Guid testId = Uuid.Next(new FakeTimeProvider(startsAt));
```

`Uuid.Create()` and every `Uuid.Next(...)` overload return a standard `Guid`:

```csharp
public record Order(Guid Id, /* ... */);

var order = new Order(Uuid.Create(), /* ... */);        // sortable primary key
```

## Why it's fast

The built-in `Guid.CreateVersion7()` reads `DateTimeOffset.UtcNow` for the timestamp and calls the OLE32
`CoCreateGuid` interop for entropy — both comparatively expensive. `Uuid` instead reads the amortized
`FastClock` for the timestamp and fills the entropy bits from `Random.Shared` (Xoshiro256\*\*), writing the 16
bytes directly with no intermediate allocation.

```
| Method               | Mean     | Allocated |
|--------------------- |---------:|----------:|
| Guid.NewGuid()       | 30.70 ns |         - |
| Guid.CreateVersion7()| 49.59 ns |         - |
| Uuid.Create()        | 20.08 ns |         - |
```

## Use cases

- **Primary keys** where you want `Guid` compatibility but better index locality than random GUIDs.
- **Event / log / message ids** that benefit from being roughly time-ordered.
- **High-throughput id minting** where `Guid.NewGuid()`/`Guid.CreateVersion7()` cost shows up in a profiler.

## Gotchas

- **Same-millisecond ordering is not guaranteed.** All bits below the 48-bit timestamp are random — there is no
  per-node counter — so two ids minted in the same millisecond have no defined order relative to each other. If
  you need a strictly increasing, node-aware id, use
  [CloudMesh.Guid64](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.Guid64) instead.
- **Not a secret.** The embedded timestamp makes creation time (and rough generation rate) observable. Don't use
  v7 UUIDs as unguessable tokens, password-reset codes, or public order numbers.
- **`Uuid.Next(long)` requires a non-negative Unix-millisecond value** and throws `ArgumentOutOfRangeException`
  otherwise.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
