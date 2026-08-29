using System.Buffers;
using System.Text;
using Xunit;

namespace CloudMesh.Xml.Tests;

public class EncodedXmlReaderTests
{
    static EncodedXmlReaderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Read_EmptyUtf8Span_ReturnsFalse()
    {
        using var reader = new EncodedXmlReader(ReadOnlySpan<byte>.Empty);
        Assert.False(reader.Read());
        Assert.Equal(Utf8XmlTokenType.None, reader.TokenType);
    }

    [Fact]
    public void Read_Utf8Xml_WithoutTranscoding_YieldsCorrectTokens()
    {
        var xml = "<root attr=\"value\"><item id=\"1\">Hello &amp; World</item><empty /></root>"u8;
        using var reader = new EncodedXmlReader(xml);

        Assert.Equal(Encoding.UTF8.CodePage, reader.DetectedEncoding.CodePage);
        Assert.Equal(Encoding.UTF8.CodePage, reader.Encoding.CodePage);

        // <root attr="value">
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("root", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.False(reader.IsEmptyElement);
        Assert.Equal(0, reader.Depth);
        Assert.True(reader.TryGetAttribute("attr"u8, out var attrVal));
        Assert.Equal("value", Encoding.UTF8.GetString(attrVal));

        // <item id="1">
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("item", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.False(reader.IsEmptyElement);
        Assert.Equal(1, reader.Depth);
        Assert.True(reader.TryGetAttribute("id"u8, out var idVal));
        Assert.Equal("1", Encoding.UTF8.GetString(idVal));

        // Hello &amp; World
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal("Hello &amp; World", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.Equal(2, reader.Depth);

        // </item>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal("item", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.Equal(1, reader.Depth);

        // <empty />
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("empty", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.IsEmptyElement);
        Assert.Equal(1, reader.Depth);

        // </root>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal("root", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.Equal(0, reader.Depth);

        Assert.False(reader.Read());
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    [InlineData("windows-1252")]
    [InlineData("iso-8859-1")]
    [InlineData("shift_jis")]
    [InlineData("gb2312")]
    [InlineData("euc-kr")]
    public void Read_VariousEncodings_ParsesXmlIdenticallyToUtf8(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var xmlTemplate = "<?xml version=\"1.0\" encoding=\"{0}\"?><catalog><book id=\"bk101\"><title>Café &amp; Résumé 🚀</title><price currency=\"EUR\">29.95</price></book></catalog>";

        // For non-Unicode encodings, replace emoji with localized text that the encoding supports
        var text = encodingName switch
        {
            "shift_jis" => xmlTemplate.Replace("Café &amp; Résumé 🚀", "日本語 &amp; テスト 本"),
            "gb2312" => xmlTemplate.Replace("Café &amp; Résumé 🚀", "简体中文 &amp; 电子书"),
            "euc-kr" => xmlTemplate.Replace("Café &amp; Résumé 🚀", "한국어 &amp; 텍스트 도서"),
            "windows-1252" or "iso-8859-1" => xmlTemplate.Replace(" 🚀", ""),
            _ => xmlTemplate
        };

        var xmlString = string.Format(text, encodingName);
        var encodedBytes = encoding.GetBytes(xmlString);

        using var reader = new EncodedXmlReader(encodedBytes);

        Assert.Equal(encoding.CodePage, reader.DetectedEncoding.CodePage);

        // Processing Instruction <?xml ... ?>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.ProcessingInstruction, reader.TokenType);

        // <catalog>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("catalog", Encoding.UTF8.GetString(reader.NameSpan));

        // <book id="bk101">
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("book", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.TryGetAttribute("id"u8, out var idVal));
        Assert.Equal("bk101", Encoding.UTF8.GetString(idVal));

        // <title>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("title", Encoding.UTF8.GetString(reader.NameSpan));

        // Title text
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);

        // </title>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal("title", Encoding.UTF8.GetString(reader.NameSpan));

        // <price currency="EUR">
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("price", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.TryGetAttribute("currency"u8, out var currVal));
        Assert.Equal("EUR", Encoding.UTF8.GetString(currVal));

        // 29.95
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal("29.95", Encoding.UTF8.GetString(reader.ValueSpan));

        // </price>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal("price", Encoding.UTF8.GetString(reader.NameSpan));

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

    [Fact]
    public void Read_Utf16LeWithBom_ParsesCorrectly()
    {
        var xmlString = "<note><to>Tove</to><from>Jani</from><heading>Reminder</heading><body>Don't forget me this weekend!</body></note>";
        var utf16WithBom = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xmlString)).ToArray();

        using var reader = new EncodedXmlReader(utf16WithBom);

        Assert.Equal(Encoding.Unicode.CodePage, reader.DetectedEncoding.CodePage);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("note", Encoding.UTF8.GetString(reader.NameSpan));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("to", Encoding.UTF8.GetString(reader.NameSpan));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal("Tove", Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal("to", Encoding.UTF8.GetString(reader.NameSpan));
    }

    [Fact]
    public void Read_Utf16BeWithBom_ParsesCorrectly()
    {
        var xmlString = "<note><message>Hello World</message></note>";
        var utf16BeWithBom = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(xmlString)).ToArray();

        using var reader = new EncodedXmlReader(utf16BeWithBom);

        Assert.Equal(Encoding.BigEndianUnicode.CodePage, reader.DetectedEncoding.CodePage);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("note", Encoding.UTF8.GetString(reader.NameSpan));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("message", Encoding.UTF8.GetString(reader.NameSpan));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal("Hello World", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void Read_WithKnownEncodingOverride_UsesSpecifiedEncoding()
    {
        var encoding = Encoding.GetEncoding("windows-1252");
        var rawXml = "<item desc=\"café\"><price>£10</price></item>";
        var bytes = encoding.GetBytes(rawXml);

        // Pass knownEncoding explicitly
        using var reader = new EncodedXmlReader(bytes, knownEncoding: encoding);

        Assert.Equal(encoding.CodePage, reader.DetectedEncoding.CodePage);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("item", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.TryGetAttribute("desc"u8, out var descVal));
        Assert.Equal("café", Encoding.UTF8.GetString(descVal));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("price", Encoding.UTF8.GetString(reader.NameSpan));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal("£10", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void Read_ReadOnlyMemory_ConstructsAndParsesProperly()
    {
        var xml = Encoding.UTF8.GetBytes("<root><child>Data</child></root>");
        var memory = new ReadOnlyMemory<byte>(xml);

        using var reader = new EncodedXmlReader(memory);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("root", Encoding.UTF8.GetString(reader.NameSpan));
    }

    [Fact]
    public void Read_ReadOnlySequence_SingleAndMultiSegment_Utf8()
    {
        var xmlString = "<root><elem a=\"1\">Test Content</elem><elem a=\"2\" /></root>";
        var bytes = Encoding.UTF8.GetBytes(xmlString);

        // Single segment
        {
            var seq = new ReadOnlySequence<byte>(bytes);
            using var reader = new EncodedXmlReader(seq);

            Assert.True(reader.Read());
            Assert.Equal("root", Encoding.UTF8.GetString(reader.NameSpan));
            Assert.True(reader.Read());
            Assert.Equal("elem", Encoding.UTF8.GetString(reader.NameSpan));
        }

        // Multi segment
        {
            var seq = CreateMultiSegmentSequence(bytes, segmentSize: 5);
            using var reader = new EncodedXmlReader(seq);

            Assert.True(reader.Read());
            Assert.Equal("root", Encoding.UTF8.GetString(reader.NameSpan));
            Assert.True(reader.Read());
            Assert.Equal("elem", Encoding.UTF8.GetString(reader.NameSpan));
            Assert.True(reader.TryGetAttribute("a"u8, out var val));
            Assert.Equal("1", Encoding.UTF8.GetString(val));
            Assert.True(reader.Read());
            Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
            Assert.Equal("Test Content", Encoding.UTF8.GetString(reader.ValueSpan));
        }
    }

    [Fact]
    public void Read_ReadOnlySequence_MultiSegment_NonUtf8()
    {
        var encoding = Encoding.Unicode;
        var xmlString = "<?xml version=\"1.0\" encoding=\"utf-16\"?><library><book id=\"123\">C# in Depth</book></library>";
        var bytes = encoding.GetBytes(xmlString);

        var seq = CreateMultiSegmentSequence(bytes, segmentSize: 8);
        using var reader = new EncodedXmlReader(seq);

        Assert.Equal(encoding.CodePage, reader.DetectedEncoding.CodePage);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.ProcessingInstruction, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("library", Encoding.UTF8.GetString(reader.NameSpan));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("book", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.TryGetAttribute("id"u8, out var idVal));
        Assert.Equal("123", Encoding.UTF8.GetString(idVal));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal("C# in Depth", Encoding.UTF8.GetString(reader.ValueSpan));
    }

    [Fact]
    public void TryReadNextAttribute_IteratesAllAttributes()
    {
        var xml = "<item a=\"1\" b=\"2\" c=\"3\" />"u8;
        using var reader = new EncodedXmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);

        var pos = 0;
        var attrs = new List<(string Name, string Value)>();
        while (reader.TryReadNextAttribute(ref pos, out var name, out var val))
        {
            attrs.Add((Encoding.UTF8.GetString(name), Encoding.UTF8.GetString(val)));
        }

        Assert.Equal(3, attrs.Count);
        Assert.Equal(("a", "1"), attrs[0]);
        Assert.Equal(("b", "2"), attrs[1]);
        Assert.Equal(("c", "3"), attrs[2]);
    }

    [Fact]
    public void Read_CDataAndCommentAndDocType_HandlesAllTokens()
    {
        var xml = "<!DOCTYPE html><root><!-- comment --><![CDATA[some <raw> content]]></root>"u8;
        using var reader = new EncodedXmlReader(xml);

        // DOCTYPE
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.DocType, reader.TokenType);
        Assert.Equal("<!DOCTYPE html>", Encoding.UTF8.GetString(reader.RawTokenSpan));

        // <root>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);

        // <!-- comment -->
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Comment, reader.TokenType);
        Assert.Equal(" comment ", Encoding.UTF8.GetString(reader.ValueSpan));

        // CDATA
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.CData, reader.TokenType);
        Assert.Equal("some <raw> content", Encoding.UTF8.GetString(reader.ValueSpan));

        // </root>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);

        Assert.False(reader.Read());
    }

    [Fact]
    public void CurrentState_And_Depth_TrackAccurately()
    {
        var xml = "<a><b><c>hello</c></b></a>"u8;
        using var reader = new EncodedXmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(0, reader.Depth);
        Assert.Equal(1, reader.CurrentState.Depth);

        Assert.True(reader.Read());
        Assert.Equal(1, reader.Depth);
        Assert.Equal(2, reader.CurrentState.Depth);

        Assert.True(reader.Read());
        Assert.Equal(2, reader.Depth);
        Assert.Equal(3, reader.CurrentState.Depth);

        Assert.True(reader.Read());
        Assert.Equal(3, reader.Depth);

        Assert.True(reader.Read());
        Assert.Equal(2, reader.Depth);
        Assert.Equal(2, reader.CurrentState.Depth);

        Assert.True(reader.Read());
        Assert.Equal(1, reader.Depth);
        Assert.Equal(1, reader.CurrentState.Depth);

        Assert.True(reader.Read());
        Assert.Equal(0, reader.Depth);
        Assert.Equal(0, reader.CurrentState.Depth);
    }

    [Fact]
    public void LargeDocument_NonUtf8_ExpandsCapacityAndParsesAllElements()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-16\"?><catalog>");
        for (int i = 0; i < 2000; i++)
        {
            sb.Append($"<item id=\"{i}\"><name>Item {i} &amp; café £50</name></item>");
        }
        sb.Append("</catalog>");

        var xmlString = sb.ToString();
        var bytes = Encoding.Unicode.GetBytes(xmlString);

        using var reader = new EncodedXmlReader(bytes);

        Assert.True(reader.Read()); // PI
        Assert.True(reader.Read()); // <catalog>
        Assert.Equal("catalog", Encoding.UTF8.GetString(reader.NameSpan));

        var itemCount = 0;
        while (reader.Read())
        {
            if (reader.TokenType == Utf8XmlTokenType.StartElement && reader.NameSpan.SequenceEqual("item"u8))
            {
                Assert.True(reader.TryGetAttribute("id"u8, out var idSpan));
                Assert.Equal(itemCount.ToString(), Encoding.UTF8.GetString(idSpan));
                itemCount++;
            }
        }

        Assert.Equal(2000, itemCount);
    }

    [Fact]
    public void TryGetAttribute_WhenNotOnStartElement_ReturnsFalse()
    {
        var xml = "<root>Some Text</root>"u8;
        using var reader = new EncodedXmlReader(xml);

        // Before reading
        Assert.False(reader.TryGetAttribute("id"u8, out _));

        // On <root>
        Assert.True(reader.Read());
        Assert.False(reader.TryGetAttribute("nonexistent"u8, out _));

        // On text
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.False(reader.TryGetAttribute("id"u8, out _));

        // On </root>
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.False(reader.TryGetAttribute("id"u8, out _));
    }

    [Fact]
    public void ChunkedStreaming_WithStatePreservation()
    {
        var part1 = "<root><item>Hello"u8;
        var part2 = " World</item></root>"u8;

        var state = default(Utf8XmlReaderState);

        // Read part 1 (non-final)
        using (var reader1 = new EncodedXmlReader(part1, isFinalBlock: false, state: state))
        {
            Assert.True(reader1.Read()); // <root>
            Assert.True(reader1.Read()); // <item>
            // Note: Text is incomplete across boundary in non-final block
            state = reader1.CurrentState;
        }

        Assert.Equal(2, state.Depth);

        // Read part 2 (final)
        using (var reader2 = new EncodedXmlReader(part2, isFinalBlock: true, state: state))
        {
            Assert.True(reader2.Read()); // Text " World"
            Assert.Equal(Utf8XmlTokenType.Text, reader2.TokenType);
            Assert.Equal(" World", Encoding.UTF8.GetString(reader2.ValueSpan));

            Assert.True(reader2.Read()); // </item>
            Assert.Equal(Utf8XmlTokenType.EndElement, reader2.TokenType);

            Assert.True(reader2.Read()); // </root>
            Assert.Equal(Utf8XmlTokenType.EndElement, reader2.TokenType);

            Assert.False(reader2.Read());
        }
    }

    [Fact]
    public void Read_NestedXmlAcrossChunks_TracksAbsoluteStartAndEndPositions()
    {
        // 1. Build a large XML document with 1025 nested <item> elements
        const int totalItems = 1025;
        var sb = new StringBuilder();
        sb.Append("<root>\n  <data>\n");
        for (var i = 1; i <= totalItems; i++)
        {
            sb.Append($"   <item id=\"{i}\">\n     <value>{i}</value>\n   </item>\n");
        }
        sb.Append("  </data>\n</root>");

        var xmlString = sb.ToString();
        var bytes = Encoding.UTF8.GetBytes(xmlString);

        // 2. Partition into 5 chunks simulating chunked streaming of a large document
        const int chunkCount = 5;
        var chunkSize = (bytes.Length + chunkCount - 1) / chunkCount;
        var chunks = new List<byte[]>();

        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, bytes.Length - offset);
            chunks.Add(bytes[offset..(offset + length)]);
        }

        Assert.Equal(5, chunks.Count);

        // 3. Stream chunks through EncodedXmlReader while tracking absolute start and end offsets
        var positions = new List<(int startPos, int endPos)>();
        var buffer = new List<byte>();
        var state = default(Utf8XmlReaderState);
        var globalOffset = 0;
        var currentItemStart = -1;

        for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            buffer.AddRange(chunk);
            var isFinalBlock = chunkIndex == chunks.Count - 1;

            using (var reader = new EncodedXmlReader(buffer.ToArray(), isFinalBlock: isFinalBlock, state: state))
            {
                while (reader.Read())
                {
                    if (reader.TokenType == Utf8XmlTokenType.StartElement && reader.NameSpan.SequenceEqual("item"u8))
                    {
                        var tokenStartInBuffer = (int)reader.BytesConsumed - reader.RawTokenSpan.Length;
                        var itemStart = globalOffset + tokenStartInBuffer;

                        if (reader.IsEmptyElement)
                        {
                            var itemEnd = globalOffset + (int)reader.BytesConsumed;
                            positions.Add((itemStart, itemEnd));
                        }
                        else
                        {
                            currentItemStart = itemStart;
                        }
                    }
                    else if (reader.TokenType == Utf8XmlTokenType.EndElement && reader.NameSpan.SequenceEqual("item"u8))
                    {
                        var itemEnd = globalOffset + (int)reader.BytesConsumed;
                        positions.Add((currentItemStart, itemEnd));
                        currentItemStart = -1;
                    }
                }

                var consumed = (int)reader.BytesConsumed;
                state = reader.CurrentState;
                globalOffset += consumed;
                buffer.RemoveRange(0, consumed);
            }
        }

        // 4. Verify all 1025 item positions were accurately recorded
        Assert.Equal(totalItems, positions.Count);

        for (var i = 1; i <= totalItems; i++)
        {
            var pos = positions[i - 1];
            var extractedSpan = bytes.AsSpan(pos.startPos, pos.endPos - pos.startPos);
            var expectedItemString = $"<item id=\"{i}\">\n     <value>{i}</value>\n   </item>";
            var expectedBytes = Encoding.UTF8.GetBytes(expectedItemString);

            Assert.True(extractedSpan.SequenceEqual(expectedBytes),
                $"Mismatch at item {i}. Expected: {expectedItemString}, Actual: {Encoding.UTF8.GetString(extractedSpan)}");
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(512)] // 512-byte small chunks
    public void Read_NestedXmlAcrossVariableChunks_TracksItemPositionsAccurately(int chunkSpecifier)
    {
        const int totalItems = 1025;
        var sb = new StringBuilder();
        sb.Append("<root>\n  <data>\n");
        for (var i = 1; i <= totalItems; i++)
        {
            sb.Append($"   <item id=\"{i}\">\n     <value>{i}</value>\n   </item>\n");
        }
        sb.Append("  </data>\n</root>");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());

        var chunkSize = chunkSpecifier <= 10
            ? (bytes.Length + chunkSpecifier - 1) / chunkSpecifier
            : chunkSpecifier;

        var chunks = new List<byte[]>();
        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, bytes.Length - offset);
            chunks.Add(bytes[offset..(offset + length)]);
        }

        var positions = new List<(int startPos, int endPos)>();
        var buffer = new List<byte>();
        var state = default(Utf8XmlReaderState);
        var globalOffset = 0;
        var currentItemStart = -1;

        for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            buffer.AddRange(chunk);
            var isFinalBlock = chunkIndex == chunks.Count - 1;

            using var reader = new EncodedXmlReader(buffer.ToArray(), isFinalBlock: isFinalBlock, state: state);
            while (reader.Read())
            {
                if (reader.TokenType == Utf8XmlTokenType.StartElement && reader.NameSpan.SequenceEqual("item"u8))
                {
                    var tokenStartInBuffer = (int)reader.BytesConsumed - reader.RawTokenSpan.Length;
                    currentItemStart = globalOffset + tokenStartInBuffer;

                    if (reader.IsEmptyElement)
                    {
                        var itemEnd = globalOffset + (int)reader.BytesConsumed;
                        positions.Add((currentItemStart, itemEnd));
                        currentItemStart = -1;
                    }
                }
                else if (reader.TokenType == Utf8XmlTokenType.EndElement && reader.NameSpan.SequenceEqual("item"u8))
                {
                    var itemEnd = globalOffset + (int)reader.BytesConsumed;
                    positions.Add((currentItemStart, itemEnd));
                    currentItemStart = -1;
                }
            }

            var consumed = (int)reader.BytesConsumed;
            state = reader.CurrentState;
            globalOffset += consumed;
            buffer.RemoveRange(0, consumed);
        }

        Assert.Equal(totalItems, positions.Count);

        for (var i = 1; i <= totalItems; i++)
        {
            var pos = positions[i - 1];
            var extractedSpan = bytes.AsSpan(pos.startPos, pos.endPos - pos.startPos);
            var expectedItemString = $"<item id=\"{i}\">\n     <value>{i}</value>\n   </item>";
            var expectedBytes = Encoding.UTF8.GetBytes(expectedItemString);

            Assert.True(extractedSpan.SequenceEqual(expectedBytes));
        }
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var encoding = Encoding.Unicode;
        var bytes = encoding.GetBytes("<test />");
        var reader = new EncodedXmlReader(bytes);

        Assert.True(reader.Read());
        Assert.Equal("test", Encoding.UTF8.GetString(reader.NameSpan));

        reader.Dispose();
        reader.Dispose(); // Multiple dispose calls should be safe
    }

    private static ReadOnlySequence<byte> CreateMultiSegmentSequence(byte[] data, int segmentSize)
    {
        if (data.Length == 0)
            return ReadOnlySequence<byte>.Empty;

        var memory = data.AsMemory();
        ChunkSegment? first = null;
        ChunkSegment? current = null;

        for (int i = 0; i < data.Length; i += segmentSize)
        {
            var length = Math.Min(segmentSize, data.Length - i);
            var slice = memory.Slice(i, length);
            var next = new ChunkSegment(slice);

            if (first is null)
            {
                first = next;
                current = next;
            }
            else
            {
                current = current!.Append(next);
            }
        }

        return new ReadOnlySequence<byte>(first!, 0, current!, current!.Memory.Length);
    }

    private sealed class ChunkSegment : ReadOnlySequenceSegment<byte>
    {
        public ChunkSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public ChunkSegment Append(ChunkSegment next)
        {
            Next = next;
            next.RunningIndex = RunningIndex + Memory.Length;
            return next;
        }
    }
}
