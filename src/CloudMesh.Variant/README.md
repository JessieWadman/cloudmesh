# CloudMesh.Variant

A **boxing-free discriminated union** for .NET — carry any primitive, `Guid`, `DateTime`, enum, small struct,
`null`, or reference in one fixed-size `Value`, and read it back out with **zero heap allocations**.

When you gather heterogeneous values into an `object?[]`, every value type gets **boxed** — a heap allocation per
element. In hot paths (parsing JSON, staging rows for a bulk loader, buffering columnar data) that is millions of
allocations and constant GC pressure, purely to shuttle `int`s and `bool`s around. `Value` stores those inline in
an overlapped struct field instead, so nothing boxes.

- **Targets:** .NET 8, 9, 10, 11 — **License:** MIT
- A `Value` is a `readonly struct` of 24 bytes on 64-bit (an 8-byte object reference plus a 16-byte inline union); a `Value[]` is a flat array with no per-element heap objects.
- `As<T>()` / `TryGetValue<T>()` round-trips honour `Nullable<T>` — a stored `int` reads back as `int?`, a stored `null` reads back as any nullable/reference type.
- On .NET 11, `Value` **is a union type** — assign and `switch` over it like a declared `union`, without the boxing a declared `union` would cost. See [Union support](#union-support-net-11).

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
| Nullable primitives | `int?`, `bool?`, `double?`, `decimal?`, `Guid?`, … (all of the above) — the has-value flag is the null-ness of the reference field, so only the underlying value is stored |
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

## Union support (.NET 11)

On .NET 11 and later, `Value` implements the C# 15 union contract, so the compiler treats it as a union type:
implicit conversions from each case type, pattern matching that unwraps to the stored value, and exhaustiveness
checking on `switch` expressions.

```csharp
Value value = 42;

var description = value switch
{
    int i => $"int: {i}",
    double d => $"double: {d}",
    Guid g => $"guid: {g}",
    string s => $"string: {s}",
    null => "no value",
    object o => $"other: {o}",
};
```

The difference from a compiler-declared `union` is the allocation profile. A declared `union` stores its
contents as a single `object?`, so **every value-type case boxes** — on creation and on every match. `Value`
implements the union contract's *non-boxing access pattern* instead: it declares a `TryGetValue` overload per
case type and a `HasValue` property, so the compiler lowers `value is int i` to a `TryGetValue(out int)` call
and `value is null` to a `HasValue` check. Neither touches the boxing `Value` accessor:

```csharp
// 1,000,000 iterations of the switch above over an inline int: 0 bytes allocated.
```

Each scalar case type is declared in its **nullable** form — `int?`, `bool?`, `DateTime?`, … — because a
`Value` can always be null, and `Value(T?)` is the constructor that models that. The union contract pairs a
nullable case type with a `TryGetValue` whose `out` parameter is the *underlying* type, so a single case type
per scalar covers both `int` and `int?` sources while `v is int i` still binds to `TryGetValue(out int)` and
never boxes:

```csharp
int? maybe = 42;
Value value = maybe;

value is int;        // true  — matched via TryGetValue(out int), stored inline, no box
value is null;       // false

Value none = (int?)null;
none is int;         // false
none is null;        // true — matched via HasValue
```

The full case-type list is `bool?`, `byte?`, `sbyte?`, `char?`, `short?`, `ushort?`, `int?`, `uint?`, `long?`,
`ulong?`, `float?`, `double?`, `decimal?`, `Guid?`, `DateTime?`, `DateTimeOffset?`, `ArraySegment<byte>`,
`ArraySegment<char>`, `string` and `object`. The two `ArraySegment` cases stay non-nullable — an absent segment
is simply a null `Value`. `object` keeps the union sound, since a `Value` can hold any reference.

Notes:

- Patterns apply to the union's **contents**, not to the `Value` struct: `value is Value` does not match, and
  `value is int` tests the stored value.
- The existing conversion operators take priority over the union conversion, so `Value v = 42` still stores
  inline exactly as it does on .NET 8–10. Everything in this section is additive; no existing API changed.
- `((IUnion)value).Value` is the standard union accessor. It boxes inline value types, by definition — use
  `TryGetValue<T>` / `As<T>` when you want the value without a box.

There is a runnable demo under [`samples/VariantSample`](../../samples/VariantSample).

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
  `Guid`), so storing a `decimal` does **not** box. Nor does `decimal?`: a `Value` records "no value" as a null
  object reference, so the `Nullable<T>` has-value flag is never stored and only the 16-byte payload has to
  fit. The same goes for `Guid?` and every other nullable primitive.
- **`ArraySegment` vs array.** A segment stores the backing array plus offset/count inline; reading it back as the
  bare array type is intentionally rejected so a segment can't silently masquerade as the whole array.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
