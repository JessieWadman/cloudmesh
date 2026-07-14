# CloudMesh.Guid64

A compact, roughly **time-sortable 64-bit id** — Twitter's Snowflake algorithm as a first-class .NET value type,
with a fixed-width, lexically sortable Crockford Base32 string form.

When a 128-bit `Guid` is more than you want to store, index, or exchange, `Guid64` gives you a single signed
`long` that is unique across up to 1024 nodes, approximately ordered by creation time, and prints as a tidy
13-character string. It implicitly converts to/from `long`, so it drops into existing numeric columns and APIs.

- **Targets:** .NET 8, 9, 10 — **License:** MIT
- Thread-safe, monotonic generation; up to **4096 ids per node per millisecond**.
- Full BCL citizen: `IEquatable`, `IComparable`, `ISpanFormattable`, `IParsable`, `ISpanParsable`.

---

## Install

```bash
dotnet add package CloudMesh.Guid64
```

The `Guid64` type lives in the `System` namespace, so no extra `using` is needed.

## Quick start

```csharp
Guid64 id = Guid64.NewGuid();

string s   = id.ToString();          // "B" default → 13-char Crockford Base32, e.g. "0CMK3H9P4A2ZR"
long   raw = id;                     // implicit conversion to long
Guid64 rt  = Guid64.Parse(s);        // round-trips from the Base32 form

// Use it as an entity key:
public record Order(Guid64 Id, /* ... */);
var order = new Order(Guid64.NewGuid(), /* ... */);
```

### Formats

| Format | Output | Notes |
|---|---|---|
| `B` *(default)* | `0CMK3H9P4A2ZR` | Crockford Base32, **fixed 13 chars**, lexically sortable. |
| `D` | `123456789012345` | Decimal of the underlying `long`. |
| `X` | `1B2C3D4E5F607` | Hexadecimal of the underlying `long`. |

```csharp
id.ToString("D");     // decimal
id.ToBase32String();  // same as ToString("B")

Span<char> buf = stackalloc char[13];
id.TryFormat(buf, out int written);      // zero-alloc formatting
```

Parsing the Base32 form is lenient per the Crockford spec: case-insensitive, `I`/`L` read as `1`, `O` as `0`,
and `-` separators are ignored.

## Clustering — set `NodeId`

Each generated id embeds a **10-bit node id (0–1023)**. In a single-process app you can ignore it. **In a
clustered/multi-instance deployment you must give each instance a distinct `NodeId`**, or two nodes can mint
colliding ids in the same millisecond:

```csharp
// e.g. from an ordinal assigned by your orchestrator / config:
Guid64.NodeId = int.Parse(Environment.GetEnvironmentVariable("POD_ORDINAL")!);
```

If you never set it, a best-effort node id is derived from the machine's network interface MAC addresses — fine
for a single box, but **not** a substitute for explicit assignment across a cluster.

## Use cases

- **Database keys** where a 64-bit, time-ordered id gives better index locality than a random `Guid` and half
  the storage.
- **Cross-system identifiers** for systems that can't exchange GUIDs but handle `long`/`bigint` natively.
- **Short, URL-safe ids** via the fixed-width Base32 form.

## Gotchas

- **Set `NodeId` per instance in a cluster** (see above). This is the single most important correctness step.
- **Not a secret.** Ids leak creation time and rough generation rate. Don't use them for invoice numbers,
  public order numbers, reset tokens, or anything that must be unguessable.
- **Sortable, not strictly sequential across nodes.** Ordering is approximate: nodes advance independently and
  the value is timestamp-major, so global order is by time then node then sequence — not a gapless sequence.
- **Epoch horizon.** The 41-bit timestamp runs ~69 years from the 2015-01-01 epoch; generation throws
  `InvalidOperationException` once that is exhausted (or if the system clock moves backwards unrecoverably).

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
