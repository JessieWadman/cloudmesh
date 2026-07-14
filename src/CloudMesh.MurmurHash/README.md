# CloudMesh.MurmurHash

Fast, well-distributed **MurmurHash** implementations for .NET — 32-bit, 64-bit, and 128-bit variants for
hashing strings, byte buffers, arrays, and sets.

MurmurHash is a non-cryptographic hash designed for speed and good distribution: ideal for sharding,
hash tables, bloom filters, consistent hashing, and deterministic bucketing. Unlike `string.GetHashCode()`,
these produce **stable values across processes and runs**, so you can persist or compare them.

- **Targets:** .NET 8, 9, 10 — **License:** MIT
- Three algorithm variants covering 32/64/128-bit output widths.
- Adapted from the [Akka.NET](https://github.com/akkadotnet/akka.net) project — see [Credits](#credits).

> **On .NET 9+ these types are marked `[Obsolete]`.** The BCL now ships `System.IO.Hashing.XxHash*`, which is
> faster and natively supported — prefer it unless you need to keep matching values you hashed with MurmurHash
> previously.

---

## Install

```bash
dotnet add package CloudMesh.MurmurHash
```

## Variants

| Type | Output | Best for |
|---|---|---|
| `MurmurHash` | 32-bit (`int`) | Strings, arrays, and order-independent set hashing; incremental hashing primitives |
| `MurmurHash2` | 64-bit (`ulong`) | Scalars, byte arrays, and strings where a wider hash is wanted (`MurmurHash64A`) |
| `MurmurHash3` | 128-bit (two `ulong`, `byte[16]`, or `Guid`) | Content fingerprints and compact deterministic keys (x64 128-bit variant) |

All types live in the `CloudMesh.Utils` namespace.

## Usage

### 32-bit — `MurmurHash`

```csharp
using CloudMesh.Utils;

int h1 = MurmurHash.StringHash("hello");          // stable across processes
int h2 = MurmurHash.ByteHash(payloadBytes);
int h3 = MurmurHash.ArrayHash(new[] { 1, 2, 3 }); // order-sensitive

// Order-independent hash of a set:
int setHash = MurmurHash.SymmetricHash(new[] { "a", "b", "c" }, seed: 0);

// Or fold values into a running hash yourself:
uint acc = MurmurHash.StartHash(seed: 42);
acc = MurmurHash.ExtendHash(acc, value: 7, MurmurHash.StartMagicA, MurmurHash.StartMagicB);
int final = (int)MurmurHash.FinalizeHash(acc);
```

### 64-bit — `MurmurHash2`

```csharp
using CloudMesh.Utils;

ulong a = MurmurHash2.HashString("hello");
ulong b = MurmurHash2.Hash(payloadBytes);
ulong c = MurmurHash2.Hash(1234UL, seed: 99);
```

### 128-bit — `MurmurHash3`

```csharp
using CloudMesh.Utils;

var (h1, h2) = MurmurHash3.ComputeHash(payload);       // two 64-bit halves
byte[] bytes = MurmurHash3.ComputeHashToBytes(payload); // 16-byte digest
Guid fingerprint = MurmurHash3.ComputeHashToGuid(payload); // compact, deterministic key
```

`ComputeHashToGuid` is a convenient way to derive a stable, collision-resistant `Guid` key from arbitrary
content.

## Use cases

- Sharding / partitioning keys deterministically across nodes or buckets.
- Content fingerprints for de-duplication and change detection.
- Bloom filters and hash tables needing fast, well-mixed hashes.
- Consistent, cross-process hashing where `GetHashCode()` (which is randomized per run) won't do.

## Gotchas

- **Not cryptographic.** Do not use for passwords, signatures, or anything security-sensitive.
- **`MurmurHash2` and `MurmurHash3` are endian-dependent** (they read the buffer through unsafe pointers), so
  values are stable on a given architecture but not guaranteed identical across differing endianness.
- **The variants are not interchangeable** — 32/64/128-bit outputs, and the x64 vs x86 MurmurHash3 layouts,
  produce different values.
- On **.NET 9+**, expect an obsolete warning nudging you toward `System.IO.Hashing.XxHash*`.

## Credits

The 32-bit `MurmurHash` implementation is adapted from the
[Akka.NET](https://github.com/akkadotnet/akka.net) project (Apache 2.0), originally authored by Aaron Stannard.
`MurmurHash3` is adapted from [Grassfed.MurmurHash3](https://github.com/judwhite/Grassfed.MurmurHash3), itself
a port of Austin Appleby's public-domain [SMHasher](https://github.com/aappleby/smhasher) reference. All credit
for the original algorithms and implementations goes to those authors.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
