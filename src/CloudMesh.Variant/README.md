# CloudMesh.Variant

A **boxing-free discriminated union** for .NET — carry any primitive, `Guid`, `DateTime`, enum, small struct,
`null`, or reference in one fixed-size `Value`, and read it back out with **zero heap allocations**.

When you gather heterogeneous values into an `object?[]`, every value type gets **boxed** — a heap allocation per
element. In hot paths (parsing JSON, staging rows for a bulk loader, buffering columnar data) that is millions of
allocations and constant GC pressure, purely to shuttle `int`s and `bool`s around. `Value` stores those inline in
an overlapped struct field instead, so nothing boxes.

- **Targets:** .NET 8, 9, 10 — **License:** MIT
- A `Value` is a `readonly struct` of 24 bytes on 64-bit (an 8-byte object reference plus a 16-byte inline union); a `Value[]` is a flat array with no per-element heap objects.
- `As<T>()` / `TryGetValue<T>()` round-trips honour `Nullable<T>` — a stored `int` reads back as `int?`, a stored `null` reads back as any nullable/reference type.

---

## Install

```bash
dotnet add package CloudMesh.Variant
```

## The problem it solves

```csharp
object?[] values = new object?[3];
values[0] = 4;     // int   → boxed (heap allocation)
values[1] = null;  // null  → no boxing
values[2] = true;  // bool  → boxed (heap allocation)
```

Two allocations, just to hold two value types. With `Value`:

```csharp
using CloudMesh.Variant;

Value[] values = new Value[3];
values[0] = 4;     // int   → no boxing (implicit conversion)
values[1] = null;  // null  → no boxing
values[2] = true;  // bool  → no boxing
```

Zero allocations. The array is a contiguous block of 16-byte structs.

## Reading values back

Use `As<T>()` (throws on mismatch) or `TryGetValue<T>()` (returns `false`):

```csharp
Value v = 42;

int i  = v.As<int>();                 // 42
int? n = v.As<int?>();                // 42  — Nullable<T> round-trips
bool ok = v.TryGetValue<int>(out i);  // true
bool no = v.TryGetValue<long>(out _); // false — no implicit widening; the stored type must match

// Explicit conversion operators exist for the built-in types too:
var back = (int)v;                    // 42
```

Nulls and reference types behave as you'd expect:

```csharp
Value empty = null;
empty.IsNull;                    // true
empty.TryGetValue<string>(out var s);   // true, s == null
empty.TryGetValue<int?>(out var maybe); // true, maybe == null
empty.TryGetValue<int>(out _);          // false — a plain int can't be null
```

Inspect what's inside with `Type`:

```csharp
Value g = Guid.NewGuid();
g.Type;   // typeof(System.Guid)  — reports the real type, not the internal storage type
```

## What stores without boxing

| Category | Examples |
|---|---|
| Primitives | `bool`, `byte`, `sbyte`, `char`, `short`/`ushort`, `int`/`uint`, `long`/`ulong`, `float`, `double` |
| Nullable primitives | `int?`, `bool?`, `double?`, … (all of the above) |
| Date/time | `DateTime`, `DateTimeOffset` (UTC and common quarter-hour offsets are packed inline) |
| Identifiers & enums | `Guid`, any `enum` |
| Buffers | `ArraySegment<byte>`, `ArraySegment<char>` |
| Small unmanaged structs | any `struct` with no references that fits in the inline union |
| Anything else | reference types & large/managed structs — held as a plain object reference (no extra allocation) |

Values whose type is only known generically go through `Value.Create<T>(value)`, which picks the inline
representation automatically:

```csharp
static Value Wrap<T>(T item) => Value.Create(item);   // no boxing for the inline-eligible types above
```

## Use cases

- **Columnar / bulk-load staging** — parse a record's fields into a `Value[]`, reorder to match the loader's
  column order, and emit, all without boxing a single scalar.
- **Generic buffers & message payloads** — pass arbitrary scalar payloads through a generic pipeline without an
  `object` box per message. (CloudMesh.DataBlocks uses `Value` internally for exactly this.)
- **Interpreters / dynamic values** — a compact cell type for a stack or register file that mixes numeric,
  boolean, and reference values.

## Gotchas

- **Exact-type match on read.** `TryGetValue<T>` does not widen or convert — a stored `int` is not readable as
  `long` or `double`. Read it as the type you stored (optionally its `Nullable<T>`), then convert yourself.
- **`As<T>()` throws** `InvalidCastException` on a mismatch; use `TryGetValue<T>` when a miss is expected.
- **`decimal` is stored inline** — it is a 16-byte unmanaged value that fits the inline union (exactly like
  `Guid`), so storing a `decimal` (or `decimal?`) does **not** box.
- **`ArraySegment` vs array.** A segment stores the backing array plus offset/count inline; reading it back as the
  bare array type is intentionally rejected so a segment can't silently masquerade as the whole array.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
