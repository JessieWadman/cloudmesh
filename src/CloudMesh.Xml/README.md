# CloudMesh.Xml

A fast, low-allocation, forward-only **XML tokenizer and transcoding toolkit** for .NET — parse UTF-8 XML directly
from spans with `Utf8XmlReader`, auto-detect character encodings with `XmlEncodingDetector`, and stream legacy
code pages with `EncodedXmlReader` and `CodePageTransformer`.

Similar to `System.Text.Json.Utf8JsonReader`, `Utf8XmlReader` is a high-performance, non-allocating `ref struct`
tokenizer that operates directly on UTF-8 byte spans without building a DOM tree. For documents using non-UTF-8
encodings (such as UTF-16, ISO-8859-1, Windows-1252, or EBCDIC), `XmlEncodingDetector` inspects BOMs and XML
declarations to identify the charset, while `EncodedXmlReader` and `CodePageTransformer` transcode the stream on
the fly into pooled buffers.

- **Targets:** .NET 8, 9, 10, 11 — **License:** MIT
- **Allocation-free UTF-8 tokenizer:** `Utf8XmlReader` parses directly from `ReadOnlySpan<byte>` with zero heap allocations.
- **Automatic encoding detection & transcoding:** Parse arbitrary XML encodings seamlessly via `EncodedXmlReader`.

---

## Install

```bash
dotnet add package CloudMesh.Xml
```

## Quick start

### Tokenizing UTF-8 XML with `Utf8XmlReader`

`Utf8XmlReader` reads XML tokens sequentially from a UTF-8 byte span:

```csharp
using System.Text;
using CloudMesh.Xml;

ReadOnlySpan<byte> xml = """
    <catalog>
        <book id="1" in-stock="true">
            <title>Designing Data-Intensive Applications</title>
        </book>
    </catalog>
    """u8;

var reader = new Utf8XmlReader(xml);

while (reader.Read())
{
    switch (reader.TokenType)
    {
        case Utf8XmlTokenType.StartElement:
            Console.WriteLine($"Element: {Encoding.UTF8.GetString(reader.NameSpan)} (Depth: {reader.Depth})");

            // Look up an attribute by name without allocating:
            if (reader.TryGetAttribute("id"u8, out var idSpan))
            {
                Console.WriteLine($"  Attribute id={Encoding.UTF8.GetString(idSpan)}");
            }
            break;

        case Utf8XmlTokenType.Text:
            Console.WriteLine($"Text: {Encoding.UTF8.GetString(reader.ValueSpan)}");
            break;

        case Utf8XmlTokenType.EndElement:
            Console.WriteLine($"End: {Encoding.UTF8.GetString(reader.NameSpan)}");
            break;
    }
}
```

### Iterating all attributes

Iterate through element attributes sequentially without allocations:

```csharp
if (reader.TokenType == Utf8XmlTokenType.StartElement)
{
    int position = 0;
    while (reader.TryReadNextAttribute(ref position, out var name, out var value))
    {
        Console.WriteLine($"  {Encoding.UTF8.GetString(name)} = {Encoding.UTF8.GetString(value)}");
    }
}
```

---

## Handling Multi-Encoding XML

### `EncodedXmlReader`

When reading XML with an unknown or non-UTF-8 character encoding, use `EncodedXmlReader`. It inspects the input
with `XmlEncodingDetector`, transcodes non-UTF-8 payloads to UTF-8 using pooled buffers, and exposes the exact
same tokenizing API as `Utf8XmlReader`:

```csharp
using System.Text;
using CloudMesh.Xml;

// Accepts ReadOnlySpan<byte>, ReadOnlyMemory<byte>, or ReadOnlySequence<byte>
using var reader = new EncodedXmlReader(rawXmlBytes);

Console.WriteLine($"Detected Encoding: {reader.DetectedEncoding.WebName}");

while (reader.Read())
{
    if (reader.TokenType == Utf8XmlTokenType.StartElement)
    {
        // Spans are always presented as UTF-8
        Console.WriteLine($"Tag: {Encoding.UTF8.GetString(reader.NameSpan)}");
    }
}
```

### `XmlEncodingDetector`

`XmlEncodingDetector` can also be used as a standalone static utility to detect the encoding of XML payloads
from `ReadOnlySpan<byte>`, `ReadOnlyMemory<byte>`, or `ReadOnlySequence<byte>`:

```csharp
using System.Text;
using CloudMesh.Xml;

Encoding encoding = XmlEncodingDetector.Detect(rawXmlBytes);
Console.WriteLine($"Encoding: {encoding.WebName}");
```

Detection evaluates:
1. **Byte Order Marks (BOM):** UTF-8, UTF-16 (BE/LE), UTF-32 (BE/LE).
2. **Multi-byte XML signatures:** UTF-16, UTF-32, and EBCDIC byte signatures without BOM.
3. **XML declarations:** `<?xml ... encoding="..."?>` headers in ASCII, UTF-8, ISO-8859, Windows-125*, UTF-16, and EBCDIC encodings.
4. **W3C Fallback:** Defaults to UTF-8 when no declaration or signature is present.

### `CodePageTransformer`

`CodePageTransformer` (in `CloudMesh.Xml.CodePages`) provides high-performance, streaming character transcoding
between any two `Encoding` instances, renting intermediate character buffers from `ArrayPool<char>` and writing
directly into any `IBufferWriter<byte>`:

```csharp
using System.Buffers;
using System.Text;
using CloudMesh.Xml.CodePages;

var sourceEncoding = Encoding.GetEncoding("windows-1252");
var destinationEncoding = Encoding.UTF8;

using var transformer = new CodePageTransformer(sourceEncoding, destinationEncoding);
var writer = new ArrayBufferWriter<byte>();

// Transcode legacy bytes to UTF-8
transformer.Transform(legacyBytes, writer, flush: true);

ReadOnlySpan<byte> utf8Bytes = writer.WrittenSpan;
```

---

## Token Types

`Utf8XmlReader` and `EncodedXmlReader` classify XML nodes into `Utf8XmlTokenType`:

| Token Type | Description |
|---|---|
| `StartElement` | Opening tag (e.g. `<book>`, `<item />`). `IsEmptyElement` is `true` for self-closing tags. |
| `EndElement` | Closing tag (e.g. `</book>`). |
| `Text` | Inner character data between elements. |
| `CData` | CDATA section content (`<![CDATA[...]]>`). |
| `Comment` | XML comment content (`<!-- ... -->`). |
| `ProcessingInstruction` | Processing instruction content (`<?...?>`). |
| `DocType` | Document type declaration (`<!DOCTYPE ...>`). |
| `None` | Uninitialized reader or end of document. |

---

## Use cases

- **High-throughput XML parsing** where `XmlDocument`, `XDocument`, or `System.Xml.XmlReader` allocations cause GC pressure.
- **Streaming XML ingest pipelines** over network streams, `PipeReader`, or `ReadOnlySequence<byte>` chunks.
- **Ingesting legacy feeds** with diverse or unknown encodings (e.g., Windows-1252, ISO-8859-1, UTF-16, EBCDIC) without manual transcoding boilerplate.
- **Zero-allocation attribute extraction** and filtering on hot ingestion paths.

## Gotchas

- **Tokenizer, not a DOM:** `Utf8XmlReader` is a forward-only tokenizer. It does not construct an object tree, validate DTDs/schemas, or expand XML entities.
- **Attribute values are raw:** Values returned from `TryGetAttribute` and `TryReadNextAttribute` contain raw character slices as they appear in the source (e.g., entity references like `&amp;` remain unescaped).
- **Span lifetime:** Spans returned by `NameSpan`, `ValueSpan`, and `RawTokenSpan` point directly into the underlying buffer; they are only valid while the source buffer remains unchanged.
- **Dispose `EncodedXmlReader` and `CodePageTransformer`:** When processing non-UTF-8 data, `EncodedXmlReader` and `CodePageTransformer` rent memory from `ArrayPool`; always dispose them (e.g., via `using`) to return rented buffers.

---

MIT © Jessie Wadman. Part of [CloudMesh](https://github.com/JessieWadman/cloudmesh).
