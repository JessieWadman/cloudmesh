# CloudMesh.Base32

A fast, **zero-allocation Crockford Base32** codec for .NET — encode integers and byte spans directly into
caller-provided buffers, with lenient, human-friendly decoding.

[Crockford Base32](https://www.crockford.com/base32.html) is a case-insensitive alphabet that omits the
confusable characters `I`, `L`, `O`, and `U`, so encoded ids are safe to read aloud, type, and put in URLs. This
package encodes into a `Span<char>` you own — no intermediate strings, no `byte[]` churn — making it ideal for
hot paths and id formatting.

- **Targets:** .NET 8, 9, 10 — **License:** MIT
- No padding (`=`) characters; always the Crockford alphabet `0123456789ABCDEFGHJKMNPQRSTVWXYZ`.
- Fixed-width integer encodings that are **lexically sortable** (same order as the numbers).

---

## Install

```bash
dotnet add package CloudMesh.Base32
```

The `Base32` type lives in the `System` namespace, so no extra `using` is needed.

## Encoding

```csharp
// Fixed-width integer forms — 13 chars for long, 7 chars for int:
Span<char> buf = stackalloc char[13];
Base32.Format(1234567890123L, buf);          // writes 13 chars, no allocation

Span<char> small = stackalloc char[7];
Base32.Format(42, small);                    // int → 7 chars

// Arbitrary bytes — size the buffer with GetBase32CharCount:
ReadOnlySpan<byte> data = stackalloc byte[] { 1, 2, 3, 4, 5 };
Span<char> dest = stackalloc char[Base32.GetBase32CharCount(data.Length)];
Base32.Format(data, dest);
```

## Decoding

Decoding is **lenient** per the Crockford spec: case-insensitive, `I`/`L` are read as `1`, `O` as `0`, and `-`
separators are ignored.

```csharp
long value = Base32.DecodeInt64("0CMK3H9P4A2ZR");        // throws FormatException on bad input
bool ok    = Base32.TryDecodeInt64(text, out long v);    // non-throwing

// Bytes — size with GetMaxByteCount:
Span<byte> bytes = stackalloc byte[Base32.GetMaxByteCount(text.Length)];
if (Base32.TryDecode(text, bytes, out int written)) { /* bytes[..written] */ }
```

Byte decoding also accepts UTF-8/ASCII `ReadOnlySpan<byte>` sources and, for pipelines, `ReadOnlySequence<char>`
/ `ReadOnlySequence<byte>` — the decoder carries 5-bit regrouping state across segments, so results are correct
regardless of where segment boundaries fall.

## Use cases

- **Compact, sortable string ids** — the fixed-width `long`/`int` encodings back
  [CloudMesh.Guid64](https://github.com/JessieWadman/cloudmesh/tree/main/src/CloudMesh.Guid64)'s default form.
- **URL- and human-friendly tokens** where you want no ambiguous characters and no case sensitivity.
- **Hot-path encoding** into pooled or stack-allocated buffers, avoiding per-call allocations.

## Gotchas

- **You size the destination.** Callers own the buffer. Use `GetBase32CharCount(byteLength)` before encoding and
  `GetMaxByteCount(charCount)` before decoding; too-small buffers throw `ArgumentException` (or return `false`
  from the `TryDecode` overloads).
- **Fixed-width sortability applies to the `Format(int/long, …)` overloads.** They always emit 7/13 characters,
  so lexical order matches numeric order. The variable-length byte encoding is not zero-padded.
- **No padding, Crockford-only.** This is not RFC 4648 Base32 — there are no `=` characters and the alphabet
  differs, so output won't interop with a standard Base32 decoder.
- **`GetMaxByteCount` is an upper bound.** When the input contains `-` separators the actual decoded length is
  smaller — always use the `bytesWritten` out value.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
