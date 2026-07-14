# CloudMesh.Timestamp

Cheap, **monotonic** timestamps for .NET — elapsed-time math that never lurches because of NTP, DST, or a VM
suspend/resume, plus a fast wall-clock reader when you actually need the time of day.

`DateTimeOffset.UtcNow` is a wall-clock reading: it can jump backwards (NTP correction, DST, manual clock set),
which quietly breaks timeout, retry, and expiry logic built on subtracting two of them. This package gives you
value types whose difference is a true, forward-only duration — and it's faster to capture, too.

- **Targets:** .NET 8, 9, 10 — **License:** MIT
- Every capture is a single `long`: zero allocation, no boxing, no kernel transition on the hot path.
- Three tools for three jobs: [`Timestamp`](#timestamp), [`HighResolutionTimestamp`](#highresolutiontimestamp),
  and [`FastClock`](#fastclock).

---

## Install

```bash
dotnet add package CloudMesh.Timestamp
```

The types live in the `System` namespace, so no extra `using` is needed.

## Which one do I want?

| Type | Source | Resolution | Use it for |
|---|---|---|---|
| `Timestamp` | `Environment.TickCount64` | ~15 ms (millisecond API) | The cheapest possible monotonic stopwatch — timeouts, retries, cache TTLs. |
| `HighResolutionTimestamp` | `Stopwatch` | sub-millisecond | Monotonic timing where same-millisecond events must be distinguishable. |
| `FastClock` | `Stopwatch`, re-anchored | millisecond | A cheap wall-clock reading that *stays accurate* over a long-running process. |

Rule of thumb: **measuring a duration → `Timestamp`/`HighResolutionTimestamp`; asking "what time is it?" →
`FastClock`.**

---

## `Timestamp`

A monotonic instant in a single `long`. Subtracting two of them yields elapsed **milliseconds** and can never be
corrupted by a clock adjustment:

```csharp
var start = Timestamp.Now;
await DoWorkAsync();
long elapsedMs = Timestamp.Now - start;      // forward-only, immune to NTP/DST

// Deadlines read naturally:
var deadline = Timestamp.Now + 5_000;        // 5 seconds from now
while (Timestamp.Now < deadline) { /* ... */ }
```

Need it as a wall-clock time? Project it:

```csharp
DateTimeOffset when = ts.ToDateTimeOffset();          // fast: fixed process-start origin (may drift)
DateTimeOffset exact = ts.ToExactDateTimeOffset();    // re-reads the system clock (accurate)
long unixMs = ts.ToUnixTimeMilliseconds();
```

## `HighResolutionTimestamp`

Same API and monotonic guarantees as `Timestamp`, but backed by `Stopwatch` for sub-millisecond precision — the
right choice when two events in the same millisecond must still order correctly (id generators, fine-grained
tracing). The arithmetic operators still speak whole milliseconds.

```csharp
var t0 = HighResolutionTimestamp.Now;
Process();
long ms = HighResolutionTimestamp.Now - t0;
```

## `FastClock`

A wall-clock reader that's nearly as cheap as reading a counter but, unlike a fixed-origin projection, **tracks
the system clock**. It reads `Stopwatch` and periodically re-anchors its origin to `DateTimeOffset.UtcNow` (by
default at most once every 5 seconds), so it catches up to NTP corrections and VM suspend/resume instead of
drifting. Re-anchoring is lock-free and amortized — only the first call after the interval elapses pays for it.

```csharp
long nowUnixMs = FastClock.UnixTimeMillisecondsNow();
DateTimeOffset now = FastClock.DateTimeOffsetNow();

// Tune how often it may re-anchor (trade freshness for even fewer system-clock reads):
FastClock.AdjustInterval = TimeSpan.FromSeconds(1);
```

This is what [CloudMesh.Uuid](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.Uuid) uses to
stamp UUID v7 values quickly and accurately.

---

## Use cases

- **Timeouts, retries, backoff, rate limiting** — durations that can't be poisoned by a clock change.
- **Cache expiration / TTLs** — compare `Timestamp.Now` against a stored deadline.
- **Sync intervals and polling loops** — "has 30s elapsed?" without `DateTime` arithmetic hazards.
- **High-throughput "what time is it?"** — `FastClock` where calling `DateTimeOffset.UtcNow` per operation
  would be too costly.

## Gotchas

- **Monotonic values are not wall-clock times.** A `Timestamp`/`HighResolutionTimestamp` is only meaningful as a
  duration until you project it. The value itself is not comparable across processes or machines.
- **Fast vs. exact projections.** `ToDateTimeOffset()`/`ToUnixTimeMilliseconds()` use a fixed origin captured at
  process start — fast, but can drift over hours/days. Use the `ToExact…` variants (or `FastClock`) when the
  projected time must track the system clock.
- **Persisting them is a mistake.** Don't store a monotonic timestamp and compare it in another process; store
  the projected `DateTimeOffset`/Unix-ms instead.
- **`FastClock.AdjustInterval` is a freshness/cost trade-off**, not a correctness knob — a larger interval means
  fewer system-clock reads but a slightly staler wall-clock reading between re-anchors.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
