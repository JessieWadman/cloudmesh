using System.Buffers;
using System.Text;
using Xunit;

namespace CloudMesh.Xml.Tests;

public class EncodedXmlReaderAdversarialTests
{
    static EncodedXmlReaderAdversarialTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static void ReadAllEncoded(byte[] bytes, bool isFinalBlock, Encoding? knownEncoding = null)
    {
        using var reader = new EncodedXmlReader(bytes, isFinalBlock: isFinalBlock, knownEncoding: knownEncoding);
        while (reader.Read())
        {
        }
    }

    // ========================================================================
    // 1. Multi-Segment ReadOnlySequence in Various Encodings
    // ========================================================================

    [Theory]
    [InlineData("utf-8", 1)]
    [InlineData("utf-8", 2)]
    [InlineData("utf-16", 1)]
    [InlineData("utf-16", 2)]
    [InlineData("utf-16BE", 1)]
    [InlineData("utf-16BE", 2)]
    [InlineData("windows-1252", 1)]
    [InlineData("windows-1252", 3)]
    [InlineData("shift_jis", 1)]
    [InlineData("shift_jis", 2)]
    [InlineData("gb2312", 1)]
    [InlineData("gb2312", 2)]
    [InlineData("euc-kr", 1)]
    [InlineData("euc-kr", 2)]
    public void Read_MultiSegmentSequence_OneAndTwoByteChunks_ParsesAllTokensAccurately(string encodingName, int segmentSize)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var xmlString = encodingName switch
        {
            "shift_jis" => "<?xml version=\"1.0\" encoding=\"shift_jis\"?><catalog id=\"cat1\"><book category=\"小説\"><title>日本語の本</title><price val=\"1500\" /></book></catalog>",
            "gb2312" => "<?xml version=\"1.0\" encoding=\"gb2312\"?><catalog id=\"cat1\"><book category=\"文学\"><title>简体中文书</title><price val=\"1500\" /></book></catalog>",
            "euc-kr" => "<?xml version=\"1.0\" encoding=\"euc-kr\"?><catalog id=\"cat1\"><book category=\"소설\"><title>한국어도서</title><price val=\"1500\" /></book></catalog>",
            "windows-1252" => "<?xml version=\"1.0\" encoding=\"windows-1252\"?><catalog id=\"cat1\"><book category=\"naïve\"><title>Café &amp; Résumé</title><price val=\"1500\" /></book></catalog>",
            _ => $"<?xml version=\"1.0\" encoding=\"{encodingName}\"?><catalog id=\"cat1\"><book category=\"general\"><title>Café &amp; Résumé 🚀</title><price val=\"1500\" /></book></catalog>"
        };

        var bytes = encoding.GetBytes(xmlString);
        var sequence = TestSequenceFactory.Create(bytes, segmentSize);

        using var reader = new EncodedXmlReader(sequence);

        Assert.Equal(encoding.CodePage, reader.DetectedEncoding.CodePage);

        // <?xml ...?>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.ProcessingInstruction, reader.TokenType);

        // <catalog id="cat1">
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("catalog", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.TryGetAttribute("id"u8, out var catId));
        Assert.Equal("cat1", Encoding.UTF8.GetString(catId));

        // <book category="...">
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("book", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.TryGetAttribute("category"u8, out _));

        // <title>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("title", Encoding.UTF8.GetString(reader.NameSpan));

        // Text
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.False(reader.ValueSpan.IsEmpty);

        // </title>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal("title", Encoding.UTF8.GetString(reader.NameSpan));

        // <price val="1500" />
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("price", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.IsEmptyElement);
        Assert.True(reader.TryGetAttribute("val"u8, out var priceVal));
        Assert.Equal("1500", Encoding.UTF8.GetString(priceVal));

        // </book>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal("book", Encoding.UTF8.GetString(reader.NameSpan));

        // </catalog>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal("catalog", Encoding.UTF8.GetString(reader.NameSpan));

        Assert.False(reader.Read());
    }

    // ========================================================================
    // 2. Malformed XML in Non-UTF-8 Encodings
    // ========================================================================

    [Theory]
    [InlineData("windows-1252", "<?xml version=\"1.0\" encoding=\"windows-1252\"?><root><unclosed")]
    [InlineData("shift_jis", "<?xml version=\"1.0\" encoding=\"shift_jis\"?><root></root extra>")]
    [InlineData("utf-16", "<?xml version=\"1.0\" encoding=\"utf-16\"?></unexpectedEnd>")]
    [InlineData("gb2312", "<?xml version=\"1.0\" encoding=\"gb2312\"?><root><![CDATA[unclosed cdata</root>")]
    public void Read_MalformedXmlInNonUtf8Encoding_ThrowsInvalidDataException(string encodingName, string malformedXml)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var bytes = encoding.GetBytes(malformedXml);

        Assert.Throws<InvalidDataException>(() =>
        {
            ReadAllEncoded(bytes, isFinalBlock: true);
        });
    }

    private static void ReadAndLookupAttribute(byte[] bytes, byte[] attrName)
    {
        using var reader = new EncodedXmlReader(bytes);
        Assert.True(reader.Read()); // <?xml ...?>
        Assert.True(reader.Read()); // <root ...>
        reader.TryGetAttribute(attrName, out _);
    }

    [Theory]
    [InlineData("windows-1252")]
    [InlineData("shift_jis")]
    [InlineData("utf-16")]
    public void AttributeLookup_UnquotedAttributeInNonUtf8Encoding_ThrowsInvalidDataException(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var xml = $"<?xml version=\"1.0\" encoding=\"{encodingName}\"?><root attr=123 />";
        var bytes = encoding.GetBytes(xml);

        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            ReadAndLookupAttribute(bytes, "attr"u8.ToArray());
        });
        Assert.Contains("XML attribute value must be quoted", ex.Message);
    }

    // ========================================================================
    // 3. Constructor Parity: Span vs Memory vs ReadOnlySequence
    // ========================================================================

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("windows-1252")]
    [InlineData("shift_jis")]
    public void Read_ConstructorParity_SpanMemorySequence_ProduceIdenticalTokenStreams(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var xmlString = $"<?xml version=\"1.0\" encoding=\"{encodingName}\"?><root a=\"1\"><item b=\"2\">Text &amp; 123</item><self /></root>";
        var bytes = encoding.GetBytes(xmlString);

        // 1. Span
        var spanTokens = new List<(Utf8XmlTokenType, string, string)>();
        using (var reader = new EncodedXmlReader(bytes.AsSpan()))
        {
            while (reader.Read())
            {
                spanTokens.Add((reader.TokenType, Encoding.UTF8.GetString(reader.NameSpan), Encoding.UTF8.GetString(reader.ValueSpan)));
            }
        }

        // 2. Memory
        var memoryTokens = new List<(Utf8XmlTokenType, string, string)>();
        using (var reader = new EncodedXmlReader(bytes.AsMemory()))
        {
            while (reader.Read())
            {
                memoryTokens.Add((reader.TokenType, Encoding.UTF8.GetString(reader.NameSpan), Encoding.UTF8.GetString(reader.ValueSpan)));
            }
        }

        // 3. Sequence (multi-segment, 2 bytes per segment)
        var seqTokens = new List<(Utf8XmlTokenType, string, string)>();
        var seq = TestSequenceFactory.Create(bytes, segmentSize: 2);
        using (var reader = new EncodedXmlReader(seq))
        {
            while (reader.Read())
            {
                seqTokens.Add((reader.TokenType, Encoding.UTF8.GetString(reader.NameSpan), Encoding.UTF8.GetString(reader.ValueSpan)));
            }
        }

        Assert.Equal(spanTokens.Count, memoryTokens.Count);
        Assert.Equal(spanTokens.Count, seqTokens.Count);

        for (int i = 0; i < spanTokens.Count; i++)
        {
            Assert.Equal(spanTokens[i], memoryTokens[i]);
            Assert.Equal(spanTokens[i], seqTokens[i]);
        }
    }

    // ========================================================================
    // 4. Large Non-UTF-8 XML Payloads
    // ========================================================================

    [Fact]
    public void Read_LargeNonUtf8Payload_ExpandsPooledBuffersAndParsesAccurately()
    {
        var encoding = Encoding.GetEncoding("windows-1252");
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"windows-1252\"?><catalog>");
        for (int i = 0; i < 2_000; i++)
        {
            sb.Append($"<item id=\"{i}\" desc=\"naïve café #{i}\">Text content number {i}</item>");
        }
        sb.Append("</catalog>");

        var bytes = encoding.GetBytes(sb.ToString());
        Assert.True(bytes.Length > 100_000);

        using var reader = new EncodedXmlReader(bytes);

        int itemCount = 0;
        while (reader.Read())
        {
            if (reader.TokenType == Utf8XmlTokenType.StartElement &&
                reader.NameSpan.SequenceEqual("item"u8))
            {
                Assert.True(reader.TryGetAttribute("id"u8, out var idSpan));
                Assert.Equal(itemCount.ToString(), Encoding.UTF8.GetString(idSpan));
                itemCount++;
            }
        }

        Assert.Equal(2_000, itemCount);
    }

    // ========================================================================
    // 5. Disposal Safety & Idempotence
    // ========================================================================

    [Fact]
    public void Dispose_CalledMultipleTimes_IsSafeAndIdempotent()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"windows-1252\"?><root/>"u8;
        var reader = new EncodedXmlReader(xml);

        Assert.True(reader.Read());
        reader.Dispose();
        reader.Dispose(); // Second dispose must not throw
    }

    // ========================================================================
    // 6. Multi-Threaded Stress Testing
    // ========================================================================

    [Fact]
    public void Read_ConcurrentEncodedXmlReaders_RentsAndReturnsBuffersSafely()
    {
        const int concurrency = 8;
        const int iterations = 100;
        var encoding = Encoding.GetEncoding("windows-1252");
        var xml = "<?xml version=\"1.0\" encoding=\"windows-1252\"?><root><item a=\"1\" b=\"2\">Café &amp; Résumé</item></root>";
        var bytes = encoding.GetBytes(xml);

        Parallel.For(0, concurrency, _ =>
        {
            for (int i = 0; i < iterations; i++)
            {
                using var reader = new EncodedXmlReader(bytes);
                int tokenCount = 0;
                while (reader.Read())
                {
                    tokenCount++;
                    if (reader.TokenType == Utf8XmlTokenType.StartElement)
                    {
                        reader.TryGetAttribute("a"u8, out ReadOnlySpan<byte> _);
                    }
                }
                Assert.True(tokenCount > 0);
            }
        });
    }
}
