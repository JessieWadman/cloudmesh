# CloudMesh.DotNotation

Read and write **deeply nested object values by string path** — `"Address.City"`,
`"Orders[0].Lines[\"sku-1\"].Quantity"` — backed by **compiled, cached accessors** instead of per-call
reflection.

Point at any object graph and get or set a value addressed by a path string. Paths are parsed once and
compiled into get/set delegates (via expression trees); those delegates, the per-type property accessors, and
collection metadata are all cached, so repeated access is fast. Writing auto-creates the objects, dictionary
entries, and list slots it needs along the way.

- **Targets:** .NET 8, 9, 10 — **License:** MIT
- Lists, arrays, and dictionaries supported, including chained indexers.
- No allocation on the read path once a value type isn't involved; compiled accessors are reused process-wide.

---

## Install

```bash
dotnet add package CloudMesh.DotNotation
```

## Path syntax

| Form | Example | Meaning |
|---|---|---|
| Property | `Customer.Name` | Navigate properties with `.` |
| List / array index | `Orders[2]` | Numeric indexer into `IList`/array |
| Dictionary key | `Tags["env"]` | Bracketed key (quotes optional) |
| Typed dictionary key | `Scores[42]`, `Ids[a1b2...]` | Key is parsed to the dictionary's key type |
| Chained | `Orders[0].Lines[1].Sku` | Any mix of the above |

Dictionary keys are converted from their string form to the dictionary's key type. Supported key types:
`string`, `int`, `long`, `Guid`, and enums.

## Quick start

```csharp
using CloudMesh;

var order = new Order();

// Writing creates missing intermediates: Customer, Lines[0], etc.
DotNotation.SetValue(order, "Customer.Name", "Ada");
DotNotation.SetValue(order, "Lines[0].Sku", "ABC");
DotNotation.SetValue(order, "Tags[\"env\"]", "prod");

// Reading returns null if anything along the path is missing.
var name = (string?)DotNotation.GetValue(order, "Customer.Name");   // "Ada"
var sku  = (string?)DotNotation.GetValue(order, "Lines[0].Sku");    // "ABC"
var miss = DotNotation.GetValue(order, "Lines[99].Sku");            // null
```

### Compile once, reuse in a loop

When you hit the same path across many instances, compile it up front:

```csharp
var namePath = DotNotation.Compile("Customer.Name");

foreach (var o in orders)
    namePath.SetValue(o, "Anonymous");
```

`Compile` is cached by path string, so `GetValue`/`SetValue` and `Compile` all share the same compiled
accessor — but holding the `CompiledPath` skips the dictionary lookup per call.

### From a strongly typed selector

Turn a member-access lambda into its path string (handy for building configuration or serializing which field
changed):

```csharp
var (path, type) = DotNotation.ToDotNotation<Order, string>(o => o.Customer.Name);
// path == "Customer.Name", type == typeof(string)
```

### `DefaultValueComparer`

A small companion helper: is a boxed value the default for its runtime type, without knowing that type at
compile time?

```csharp
DefaultValueComparer.IsDefaultValue(0);                                   // true
DefaultValueComparer.IsDefaultValue("");                                  // false
DefaultValueComparer.IsDefaultValue("", emptyStringsAsDefault: true);     // true
DefaultValueComparer.IsDefaultValue(new object());                        // false (non-null ref)
```

## Use cases

- Generic mappers, projections, and rules engines that address fields by configured path.
- Serialization/diffing layers that record "property X changed" as a stable string.
- Effective-dated records — this package is the engine behind
  [CloudMesh.Temporal](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.Temporal).

## Gotchas

- **Writes auto-vivify.** Setting `Lines[0].Sku` on an empty graph creates `Lines`, grows it, and instantiates
  `Lines[0]`. Missing reference-typed intermediates are created with their **parameterless constructor** — a
  type without one will throw.
- **Reads are null-tolerant, not throw-tolerant on type errors.** A missing element yields `null`, but a value
  that can't be assigned to the target property (wrong type) throws.
- **Property names are case-sensitive** and must be public instance properties. An unknown property throws
  `InvalidOperationException`.
- **List indexers grow the list.** Writing `Items[5]` on a shorter list pads it with default elements.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
