using System.Text;
using Xunit;

namespace CloudMesh.Xml.Tests;

public class Utf8XmlReaderAdversarialTests
{
    private static void ReadAll(byte[] bytes, bool isFinalBlock)
    {
        var reader = new Utf8XmlReader(bytes, isFinalBlock);
        while (reader.Read())
        {
        }
    }

    private static void ReadAndLookupAttribute(byte[] bytes, byte[] attrName)
    {
        var reader = new Utf8XmlReader(bytes, isFinalBlock: true);
        if (reader.Read())
        {
            reader.TryGetAttribute(attrName, out _);
        }
    }

    // ========================================================================
    // 1. Truncated & Incomplete Tokens (isFinalBlock = true vs false)
    // ========================================================================

    [Theory]
    [InlineData("<")]
    [InlineData("<!")]
    [InlineData("<!-")]
    [InlineData("<!--")]
    [InlineData("<!-- unclosed comment")]
    [InlineData("<!-- unclosed comment --")]
    [InlineData("<![")]
    [InlineData("<![C")]
    [InlineData("<![CD")]
    [InlineData("<![CDA")]
    [InlineData("<![CDAT")]
    [InlineData("<![CDATA")]
    [InlineData("<![CDATA[")]
    [InlineData("<![CDATA[ unclosed cdata")]
    [InlineData("<![CDATA[ unclosed cdata ]")]
    [InlineData("<![CDATA[ unclosed cdata ]]")]
    [InlineData("<?")]
    [InlineData("<?pi")]
    [InlineData("<?pi unclosed pi")]
    [InlineData("<?pi unclosed pi ?")]
    [InlineData("<!D")]
    [InlineData("<!DO")]
    [InlineData("<!DOC")]
    [InlineData("<!DOCT")]
    [InlineData("<!DOCTY")]
    [InlineData("<!DOCTYP")]
    [InlineData("<!DOCTYPE")]
    [InlineData("<!DOCTYPE unclosed")]
    [InlineData("<!DOCTYPE html [ <!ELEMENT a ANY>")]
    [InlineData("<tag")]
    [InlineData("<tag attr=\"val\"")]
    [InlineData("</")]
    [InlineData("</tag")]
    public void Read_IncompleteTokens_FinalBlock_ThrowsInvalidDataException(string malformedXml)
    {
        var bytes = Encoding.UTF8.GetBytes(malformedXml);
        Assert.Throws<InvalidDataException>(() => ReadAll(bytes, isFinalBlock: true));
    }

    [Theory]
    [InlineData("<")]
    [InlineData("<!")]
    [InlineData("<!-- partial comment")]
    [InlineData("<![CDATA[ partial cdata")]
    [InlineData("<?pi partial pi")]
    [InlineData("<!DOCTYPE partial doctype")]
    [InlineData("<tag")]
    [InlineData("<tag attr=\"val\"")]
    [InlineData("</tag")]
    public void Read_IncompleteTokens_NonFinalBlock_ReturnsFalseGracefully(string partialXml)
    {
        var bytes = Encoding.UTF8.GetBytes(partialXml);
        var reader = new Utf8XmlReader(bytes, isFinalBlock: false);

        // In a non-final block, incomplete trailing tokens return false without throwing
        Assert.False(reader.Read());
    }

    // ========================================================================
    // 2. Malformed Element Names & Syntaxes
    // ========================================================================

    [Theory]
    [InlineData("<>")]
    [InlineData("< >")]
    [InlineData("<\t>")]
    [InlineData("<\r\n>")]
    public void Read_EmptyStartElementName_ThrowsInvalidDataException(string xmlString)
    {
        var bytes = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() => ReadAll(bytes, isFinalBlock: true));
        Assert.Contains("Empty XML element name", ex.Message);
    }

    [Theory]
    [InlineData("</>")]
    [InlineData("</ >")]
    [InlineData("</\t>")]
    public void Read_EmptyEndElementName_ThrowsInvalidDataException(string xmlString)
    {
        var bytes = Encoding.UTF8.GetBytes("<root>" + xmlString);
        var ex = Assert.Throws<InvalidDataException>(() => ReadAll(bytes, isFinalBlock: true));
        Assert.Contains("Empty XML end element name", ex.Message);
    }

    [Theory]
    [InlineData("</root extra>")]
    [InlineData("</root attr=\"val\">")]
    [InlineData("</root / >")]
    [InlineData("</root 123>")]
    public void Read_MalformedEndElement_ThrowsInvalidDataException(string malformedEndTag)
    {
        var bytes = Encoding.UTF8.GetBytes("<root>" + malformedEndTag);
        var ex = Assert.Throws<InvalidDataException>(() => ReadAll(bytes, isFinalBlock: true));
        Assert.Contains("Malformed XML end element", ex.Message);
    }

    [Fact]
    public void Read_UnexpectedEndElement_AtDepthZero_ThrowsInvalidDataException()
    {
        byte[] bytes = "</orphan>"u8.ToArray();
        var ex = Assert.Throws<InvalidDataException>(() => ReadAll(bytes, isFinalBlock: true));
        Assert.Contains("Unexpected XML end element", ex.Message);
    }

    [Fact]
    public void Read_UnsupportedDeclaration_ThrowsInvalidDataException()
    {
        byte[] bytes = "<!UNKNOWN declaration>"u8.ToArray();
        var ex = Assert.Throws<InvalidDataException>(() => ReadAll(bytes, isFinalBlock: true));
        Assert.Contains("Unsupported XML declaration", ex.Message);
    }

    // ========================================================================
    // 3. Adversarial Attribute Parsing
    // ========================================================================

    [Theory]
    [InlineData("<root =\"val\"/>")]
    [InlineData("<root = \"val\"/>")]
    [InlineData("<root attr/>")]
    [InlineData("<root attr />")]
    [InlineData("<root attr val/>")]
    [InlineData("<root attr1=\"1\" attr2 />")]
    public void AttributeLookup_MalformedAttributes_ThrowsInvalidDataException(string xmlString)
    {
        var bytes = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() => ReadAndLookupAttribute(bytes, "attr"u8.ToArray()));
        Assert.Contains("Malformed XML attribute", ex.Message);
    }

    [Theory]
    [InlineData("<root attr=val />")]
    [InlineData("<root attr=123 />")]
    [InlineData("<root attr=true />")]
    public void AttributeLookup_UnquotedAttributeValue_ThrowsInvalidDataException(string xmlString)
    {
        var bytes = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() => ReadAndLookupAttribute(bytes, "attr"u8.ToArray()));
        Assert.Contains("XML attribute value must be quoted", ex.Message);
    }

    [Fact]
    public void AttributeLookup_NonStartElementToken_ReturnsFalse()
    {
        var xml = "<root><!-- comment --><![CDATA[cdata]]>text</root>"u8;
        var reader = new Utf8XmlReader(xml, isFinalBlock: true);

        while (reader.Read())
        {
            if (reader.TokenType != Utf8XmlTokenType.StartElement)
            {
                Assert.False(reader.TryGetAttribute("attr"u8, out var val1));
                Assert.True(val1.IsEmpty);

                int pos = 0;
                Assert.False(reader.TryReadNextAttribute(ref pos, out var name2, out var val2));
                Assert.True(name2.IsEmpty);
                Assert.True(val2.IsEmpty);
            }
        }
    }

    [Fact]
    public void TryReadNextAttribute_InvalidPosition_ReturnsFalse()
    {
        var xml = "<root a=\"1\" b=\"2\" />"u8;
        var reader = new Utf8XmlReader(xml, isFinalBlock: true);

        Assert.True(reader.Read());

        int negativePos = -10;
        Assert.False(reader.TryReadNextAttribute(ref negativePos, out _, out _));

        int hugePos = 99999;
        Assert.False(reader.TryReadNextAttribute(ref hugePos, out _, out _));
    }

    [Fact]
    public void AttributeLookup_DuplicateAttributes_ExtractsFirstAndIteratesAll()
    {
        var xml = "<root id=\"first\" id=\"second\" id=\"third\" />"u8;
        var reader = new Utf8XmlReader(xml, isFinalBlock: true);

        Assert.True(reader.Read());

        // TryGetAttribute returns the first occurrence
        Assert.True(reader.TryGetAttribute("id"u8, out var firstVal));
        Assert.Equal("first", Encoding.UTF8.GetString(firstVal));

        // TryReadNextAttribute sequentially visits all 3 attributes
        var values = new List<string>();
        int pos = 0;
        while (reader.TryReadNextAttribute(ref pos, out var name, out var val))
        {
            Assert.Equal("id", Encoding.UTF8.GetString(name));
            values.Add(Encoding.UTF8.GetString(val));
        }

        Assert.Equal(["first", "second", "third"], values);
    }

    [Fact]
    public void AttributeLookup_EmptyAndLargeAttributes()
    {
        var largeValue = new string('A', 50_000);
        var xml = $"<elem emptyDouble=\"\" emptySingle='' large=\"{largeValue}\" mixed=\"'inner'\" />";
        var bytes = Encoding.UTF8.GetBytes(xml);
        var reader = new Utf8XmlReader(bytes, isFinalBlock: true);

        Assert.True(reader.Read());

        Assert.True(reader.TryGetAttribute("emptyDouble"u8, out var emptyD));
        Assert.Equal(0, emptyD.Length);

        Assert.True(reader.TryGetAttribute("emptySingle"u8, out var emptyS));
        Assert.Equal(0, emptyS.Length);

        Assert.True(reader.TryGetAttribute("large"u8, out var large));
        Assert.Equal(50_000, large.Length);
        Assert.Equal(largeValue, Encoding.UTF8.GetString(large));

        Assert.True(reader.TryGetAttribute("mixed"u8, out var mixed));
        Assert.Equal("'inner'", Encoding.UTF8.GetString(mixed));
    }

    [Fact]
    public void AttributeLookup_HundredsOfAttributes_ParsesSuccessfully()
    {
        var sb = new StringBuilder("<root ");
        for (int i = 0; i < 300; i++)
        {
            sb.Append($"attr_{i}=\"value_{i}\" ");
        }
        sb.Append("/>");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var reader = new Utf8XmlReader(bytes, isFinalBlock: true);

        Assert.True(reader.Read());

        for (int i = 0; i < 300; i++)
        {
            var attrName = Encoding.UTF8.GetBytes($"attr_{i}");
            Assert.True(reader.TryGetAttribute(attrName, out var attrVal));
            Assert.Equal($"value_{i}", Encoding.UTF8.GetString(attrVal));
        }

        int pos = 0;
        int count = 0;
        while (reader.TryReadNextAttribute(ref pos, out var name, out var val))
        {
            Assert.Equal($"attr_{count}", Encoding.UTF8.GetString(name));
            Assert.Equal($"value_{count}", Encoding.UTF8.GetString(val));
            count++;
        }
        Assert.Equal(300, count);
    }

    // ========================================================================
    // 4. Extreme Depth & Large Payloads
    // ========================================================================

    [Fact]
    public void Read_DeeplyNestedElements_TracksDepthAccurately()
    {
        const int maxDepth = 1_500;
        var sb = new StringBuilder();
        for (int i = 0; i < maxDepth; i++)
        {
            sb.Append($"<level_{i}>");
        }
        sb.Append("Leaf Content");
        for (int i = maxDepth - 1; i >= 0; i--)
        {
            sb.Append($"</level_{i}>");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var reader = new Utf8XmlReader(bytes, isFinalBlock: true);

        for (int i = 0; i < maxDepth; i++)
        {
            Assert.True(reader.Read());
            Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
            Assert.Equal($"level_{i}", Encoding.UTF8.GetString(reader.NameSpan));
            Assert.Equal(i, reader.Depth);
            Assert.Equal(i + 1, reader.CurrentState.Depth);
        }

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal("Leaf Content", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.Equal(maxDepth, reader.Depth);

        for (int i = maxDepth - 1; i >= 0; i--)
        {
            Assert.True(reader.Read());
            Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
            Assert.Equal($"level_{i}", Encoding.UTF8.GetString(reader.NameSpan));
            Assert.Equal(i, reader.Depth);
            Assert.Equal(i, reader.CurrentState.Depth);
        }

        Assert.False(reader.Read());
    }

    [Fact]
    public void Read_LargePayloadNodes_ParsesAccurately()
    {
        var largeText = new string('X', 500_000);
        var xml = $"<root><cdata><![CDATA[{largeText}]]></cdata><!--{largeText}--><text>{largeText}</text></root>";
        var bytes = Encoding.UTF8.GetBytes(xml);
        var reader = new Utf8XmlReader(bytes, isFinalBlock: true);

        Assert.True(reader.Read()); // <root>
        Assert.True(reader.Read()); // <cdata>
        Assert.True(reader.Read()); // CDATA
        Assert.Equal(Utf8XmlTokenType.CData, reader.TokenType);
        Assert.Equal(500_000, reader.ValueSpan.Length);

        Assert.True(reader.Read()); // </cdata>
        Assert.True(reader.Read()); // Comment
        Assert.Equal(Utf8XmlTokenType.Comment, reader.TokenType);
        Assert.Equal(500_000, reader.ValueSpan.Length);

        Assert.True(reader.Read()); // <text>
        Assert.True(reader.Read()); // Text
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal(500_000, reader.ValueSpan.Length);

        Assert.True(reader.Read()); // </text>
        Assert.True(reader.Read()); // </root>
        Assert.False(reader.Read());
    }

    // ========================================================================
    // 5. Complex DOCTYPE Parsing
    // ========================================================================

    [Theory]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("<!DOCTYPE root SYSTEM \"root.dtd\">")]
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">")]
    [InlineData("<!DOCTYPE doc [ <!ELEMENT doc ANY> <!ENTITY foo \"bar\"> ]>")]
    [InlineData("<!DOCTYPE doc [ <!ENTITY foo \"]>\"> ]>")] // Quotes containing closing bracket
    [InlineData("<!DOCTYPE doc [ <!ENTITY foo \">\"> ]>")] // Quotes containing closing angle bracket
    [InlineData("<!DOCTYPE doc [ [ [ ] ] ]>")] // Nested brackets
    public void Read_ComplexDocType_ParsesSuccessfully(string docType)
    {
        var xml = docType + "<root/>";
        var bytes = Encoding.UTF8.GetBytes(xml);
        var reader = new Utf8XmlReader(bytes, isFinalBlock: true);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.DocType, reader.TokenType);
        Assert.Equal(docType, Encoding.UTF8.GetString(reader.RawTokenSpan));

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("root", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.IsEmptyElement);

        Assert.False(reader.Read());
    }

    // ========================================================================
    // 6. Systematic Byte-by-Byte Streaming Fuzzer
    // ========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(32)]
    public void StreamFuzzer_ChunkedReading_MatchesCompleteParsing(int chunkSize)
    {
        var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><!DOCTYPE catalog SYSTEM \"catalog.dtd\"><catalog name=\"Main &amp; Co\"><!-- comment --><book id=\"bk101\" price=\"19.99\"><title>C# in Depth</title><summary><![CDATA[All about <generics> & async!]]></summary><tag selfClosing=\"true\" /><?custom-pi target data?></book></catalog>";
        var fullBytes = Encoding.UTF8.GetBytes(xml);

        // 1. Collect reference tokens from full single-pass parse
        var referenceTokens = new List<(Utf8XmlTokenType Type, string Name, string Value, string Raw, int Depth, bool IsEmpty)>();
        var refReader = new Utf8XmlReader(fullBytes, isFinalBlock: true);
        while (refReader.Read())
        {
            referenceTokens.Add((
                refReader.TokenType,
                Encoding.UTF8.GetString(refReader.NameSpan),
                Encoding.UTF8.GetString(refReader.ValueSpan),
                Encoding.UTF8.GetString(refReader.RawTokenSpan),
                refReader.Depth,
                refReader.IsEmptyElement));
        }

        // 2. Stream through buffer with chunking
        var streamedTokens = new List<(Utf8XmlTokenType Type, string Name, string Value, string Raw, int Depth, bool IsEmpty)>();
        var buffer = new List<byte>();
        var state = default(Utf8XmlReaderState);
        int offset = 0;

        while (offset < fullBytes.Length)
        {
            int toTake = Math.Min(chunkSize, fullBytes.Length - offset);
            buffer.AddRange(fullBytes.AsSpan(offset, toTake).ToArray());
            offset += toTake;

            bool isFinal = offset >= fullBytes.Length;
            var reader = new Utf8XmlReader(buffer.ToArray(), isFinalBlock: isFinal, state: state);

            while (reader.Read())
            {
                streamedTokens.Add((
                    reader.TokenType,
                    Encoding.UTF8.GetString(reader.NameSpan),
                    Encoding.UTF8.GetString(reader.ValueSpan),
                    Encoding.UTF8.GetString(reader.RawTokenSpan),
                    reader.Depth,
                    reader.IsEmptyElement));
            }

            state = reader.CurrentState;
            int consumed = (int)reader.BytesConsumed;
            buffer.RemoveRange(0, consumed);
        }

        Assert.Equal(referenceTokens.Count, streamedTokens.Count);
        for (int i = 0; i < referenceTokens.Count; i++)
        {
            var expected = referenceTokens[i];
            var actual = streamedTokens[i];

            Assert.Equal(expected.Type, actual.Type);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Value, actual.Value);
            Assert.Equal(expected.Raw, actual.Raw);
            Assert.Equal(expected.Depth, actual.Depth);
            Assert.Equal(expected.IsEmpty, actual.IsEmpty);
        }
    }

    // ========================================================================
    // 7. Binary Bytes, Nulls & Special Characters
    // ========================================================================

    [Fact]
    public void Read_XmlWithNullBytesInTextAndCData_PreservesBytes()
    {
        byte[] xml = [
            (byte)'<', (byte)'r', (byte)'>',
            (byte)'\0', (byte)'a', (byte)'\0',
            (byte)'<', (byte)'!', (byte)'[', (byte)'C', (byte)'D', (byte)'A', (byte)'T', (byte)'A', (byte)'[',
            (byte)'\0', (byte)'b', (byte)'\0',
            (byte)']', (byte)']', (byte)'>',
            (byte)'<', (byte)'/', (byte)'r', (byte)'>'
        ];

        var reader = new Utf8XmlReader(xml, isFinalBlock: true);

        Assert.True(reader.Read()); // <r>
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);

        Assert.True(reader.Read()); // \0a\0
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal(3, reader.ValueSpan.Length);
        Assert.Equal(0, reader.ValueSpan[0]);
        Assert.Equal((byte)'a', reader.ValueSpan[1]);
        Assert.Equal(0, reader.ValueSpan[2]);

        Assert.True(reader.Read()); // CDATA
        Assert.Equal(Utf8XmlTokenType.CData, reader.TokenType);
        Assert.Equal(3, reader.ValueSpan.Length);
        Assert.Equal(0, reader.ValueSpan[0]);
        Assert.Equal((byte)'b', reader.ValueSpan[1]);
        Assert.Equal(0, reader.ValueSpan[2]);

        Assert.True(reader.Read()); // </r>
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);

        Assert.False(reader.Read());
    }
}
