using System.Text;
using Xunit;

namespace CloudMesh.Xml.Tests;

public class Utf8XmlReaderTests
{
    // ========================================================================
    // 1. Basic Tokenization & Token Types
    // ========================================================================

    [Fact]
    public void Read_EmptySpan_ReturnsFalse()
    {
        var reader = new Utf8XmlReader(ReadOnlySpan<byte>.Empty);

        Assert.False(reader.Read());
        Assert.Equal(Utf8XmlTokenType.None, reader.TokenType);
        Assert.Equal(0, reader.Depth);
        Assert.Equal(0, reader.BytesConsumed);
        Assert.True(reader.NameSpan.IsEmpty);
        Assert.True(reader.ValueSpan.IsEmpty);
        Assert.True(reader.RawTokenSpan.IsEmpty);
        Assert.False(reader.IsEmptyElement);
    }

    [Fact]
    public void Read_StartElement_ParsesNameAndRawToken()
    {
        var xml = "<catalog>"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("catalog", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.Equal("<catalog>", Encoding.UTF8.GetString(reader.RawTokenSpan));
        Assert.True(reader.ValueSpan.IsEmpty);
        Assert.False(reader.IsEmptyElement);
        Assert.Equal(0, reader.Depth);
        Assert.Equal(xml.Length, reader.BytesConsumed);

        Assert.False(reader.Read());
    }

    [Theory]
    [InlineData("<item/>", "item", "<item/>")]
    [InlineData("<item />", "item", "<item />")]
    [InlineData("<item  />", "item", "<item  />")]
    [InlineData("<item id=\"1\"/>", "item", "<item id=\"1\"/>")]
    [InlineData("<item id=\"1\" />", "item", "<item id=\"1\" />")]
    public void Read_SelfClosingElement_SetsIsEmptyElementTrue(string xmlString, string expectedName, string expectedRaw)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal(expectedName, Encoding.UTF8.GetString(reader.NameSpan));
        Assert.Equal(expectedRaw, Encoding.UTF8.GetString(reader.RawTokenSpan));
        Assert.True(reader.IsEmptyElement);
        Assert.Equal(0, reader.Depth);
        Assert.Equal(0, reader.CurrentState.Depth);

        Assert.False(reader.Read());
    }

    [Theory]
    [InlineData("<root></root>", "root")]
    [InlineData("<root></root >", "root")]
    [InlineData("<root></ root >", "root")]
    [InlineData("<root></  root  >", "root")]
    public void Read_EndElement_ParsesNameAndDecrementsDepth(string xmlString, string expectedName)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal(0, reader.Depth);
        Assert.Equal(1, reader.CurrentState.Depth);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal(expectedName, Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.ValueSpan.IsEmpty);
        Assert.False(reader.IsEmptyElement);
        Assert.Equal(0, reader.Depth);
        Assert.Equal(0, reader.CurrentState.Depth);

        Assert.False(reader.Read());
    }

    [Fact]
    public void Read_Text_ParsesValueAndRawToken()
    {
        var xml = "Plain text content"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Equal("Plain text content", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.Equal("Plain text content", Encoding.UTF8.GetString(reader.RawTokenSpan));
        Assert.True(reader.NameSpan.IsEmpty);
        Assert.Equal(xml.Length, reader.BytesConsumed);

        Assert.False(reader.Read());
    }

    [Fact]
    public void Read_CData_ParsesValueAndRawToken()
    {
        var xml = "<![CDATA[<unescaped> & \"data\" ]]]]>"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.CData, reader.TokenType);
        Assert.Equal("<unescaped> & \"data\" ]]", Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.Equal("<![CDATA[<unescaped> & \"data\" ]]]]>", Encoding.UTF8.GetString(reader.RawTokenSpan));
        Assert.True(reader.NameSpan.IsEmpty);
        Assert.Equal(xml.Length, reader.BytesConsumed);

        Assert.False(reader.Read());
    }

    [Theory]
    [InlineData("<!-- simple comment -->", " simple comment ")]
    [InlineData("<!--comment-->", "comment")]
    [InlineData("<!-- comment with -- inside -->", " comment with -- inside ")]
    [InlineData("<!-- multi\nline\ncomment -->", " multi\nline\ncomment ")]
    public void Read_Comment_ParsesValueAndRawToken(string xmlString, string expectedValue)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Comment, reader.TokenType);
        Assert.Equal(expectedValue, Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.Equal(xmlString, Encoding.UTF8.GetString(reader.RawTokenSpan));
        Assert.True(reader.NameSpan.IsEmpty);

        Assert.False(reader.Read());
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?>", "xml version=\"1.0\" encoding=\"utf-8\"")]
    [InlineData("<?custom-pi data?>", "custom-pi data")]
    [InlineData("<?pi?>", "pi")]
    public void Read_ProcessingInstruction_ParsesValueAndRawToken(string xmlString, string expectedValue)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.ProcessingInstruction, reader.TokenType);
        Assert.Equal(expectedValue, Encoding.UTF8.GetString(reader.ValueSpan));
        Assert.Equal(xmlString, Encoding.UTF8.GetString(reader.RawTokenSpan));
        Assert.True(reader.NameSpan.IsEmpty);

        Assert.False(reader.Read());
    }

    [Theory]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("<!DOCTYPE root SYSTEM \"test.dtd\">")]
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">")]
    [InlineData("<!DOCTYPE note [ <!ELEMENT note (to,from,heading,body)> ]>")]
    [InlineData("<!DOCTYPE note [\n  <!ENTITY copy \"&#169;\">\n  <!-- comment -->\n  <!ELEMENT note (to,from,heading,body)>\n]>")]
    [InlineData("<!DOCTYPE note [ <!ENTITY quote \"test > inside [quote]\"> ]>")]
    public void Read_DocType_ParsesRawTokenAndTokenType(string doctypeString)
    {
        var xml = Encoding.UTF8.GetBytes(doctypeString);
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.DocType, reader.TokenType);
        Assert.Equal(doctypeString, Encoding.UTF8.GetString(reader.RawTokenSpan));
        Assert.True(reader.NameSpan.IsEmpty);
        Assert.True(reader.ValueSpan.IsEmpty);
        Assert.False(reader.IsEmptyElement);
        Assert.Equal(0, reader.Depth);
        Assert.Equal(xml.Length, reader.BytesConsumed);

        Assert.False(reader.Read());
    }

    [Fact]
    public void Read_DocumentWithDocType_ParsesFullDocumentTokensCorrectly()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE note SYSTEM "note.dtd"[
              <!ELEMENT note (to,from,heading,body)>
              <!ENTITY author "John Doe">
            ]>
            <note>
              <to>Tove</to>
              <from>Jani</from>
              <heading>Reminder</heading>
              <body>Don't forget me this weekend!</body>
            </note>
            """u8;

        var reader = new Utf8XmlReader(xml);
        var tokenTypes = new List<Utf8XmlTokenType>();

        while (reader.Read())
        {
            tokenTypes.Add(reader.TokenType);
            if (reader.TokenType == Utf8XmlTokenType.DocType)
            {
                Assert.StartsWith("<!DOCTYPE note", Encoding.UTF8.GetString(reader.RawTokenSpan));
                Assert.EndsWith("]>", Encoding.UTF8.GetString(reader.RawTokenSpan));
            }
        }

        Assert.Equal(Utf8XmlTokenType.ProcessingInstruction, tokenTypes[0]);
        Assert.Equal(Utf8XmlTokenType.DocType, tokenTypes[2]); // token 1 is whitespace text
        Assert.Contains(Utf8XmlTokenType.StartElement, tokenTypes);
        Assert.Contains(Utf8XmlTokenType.EndElement, tokenTypes);
    }

    // ========================================================================
    // 2. Attributes Handling
    // ========================================================================

    [Fact]
    public void TryGetAttribute_VariousAttributes_FindsExactMatches()
    {
        var xml = "<element id=\"123\" type='primary' active=\"true\" empty=\"\" desc='with \"double\" quotes' extra=\"with 'single' quotes\"/>"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader.TokenType);

        Assert.True(reader.TryGetAttribute("id"u8, out var idVal));
        Assert.Equal("123", Encoding.UTF8.GetString(idVal));

        Assert.True(reader.TryGetAttribute("type"u8, out var typeVal));
        Assert.Equal("primary", Encoding.UTF8.GetString(typeVal));

        Assert.True(reader.TryGetAttribute("active"u8, out var activeVal));
        Assert.Equal("true", Encoding.UTF8.GetString(activeVal));

        Assert.True(reader.TryGetAttribute("empty"u8, out var emptyVal));
        Assert.Equal("", Encoding.UTF8.GetString(emptyVal));
        Assert.True(emptyVal.IsEmpty);

        Assert.True(reader.TryGetAttribute("desc"u8, out var descVal));
        Assert.Equal("with \"double\" quotes", Encoding.UTF8.GetString(descVal));

        Assert.True(reader.TryGetAttribute("extra"u8, out var extraVal));
        Assert.Equal("with 'single' quotes", Encoding.UTF8.GetString(extraVal));

        // Non-existent attributes or prefix matches
        Assert.False(reader.TryGetAttribute("i"u8, out _));
        Assert.False(reader.TryGetAttribute("id2"u8, out _));
        Assert.False(reader.TryGetAttribute("nonexistent"u8, out _));
    }

    [Fact]
    public void TryGetAttribute_AttributeWithWhitespaceAroundEquals_ParsedCorrectly()
    {
        var xml = "<element   attr1  =  \"val1\"   attr2 = 'val2'   attr3=\"val3\"   />"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.True(reader.TryGetAttribute("attr1"u8, out var v1));
        Assert.Equal("val1", Encoding.UTF8.GetString(v1));

        Assert.True(reader.TryGetAttribute("attr2"u8, out var v2));
        Assert.Equal("val2", Encoding.UTF8.GetString(v2));

        Assert.True(reader.TryGetAttribute("attr3"u8, out var v3));
        Assert.Equal("val3", Encoding.UTF8.GetString(v3));
    }

    [Fact]
    public void TryGetAttribute_AttributeValueContainingAngleBrackets_ParsedCorrectly()
    {
        var xml = "<element query=\"a > b && c < d\" flag='1'/>"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.True(reader.TryGetAttribute("query"u8, out var qVal));
        Assert.Equal("a > b && c < d", Encoding.UTF8.GetString(qVal));

        Assert.True(reader.TryGetAttribute("flag"u8, out var fVal));
        Assert.Equal("1", Encoding.UTF8.GetString(fVal));
    }

    [Fact]
    public void TryGetAttribute_CalledOnNonStartElement_ReturnsFalse()
    {
        var xml = "some text"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.False(reader.TryGetAttribute("any"u8, out _));
    }

    [Fact]
    public void TryReadNextAttribute_IteratesAllAttributesInOrder()
    {
        var xml = "<item sku=\"A-100\" price=\"19.99\" inStock='true' />"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());

        var attributes = new List<(string Name, string Value)>();
        var position = 0;
        while (reader.TryReadNextAttribute(ref position, out var name, out var value))
        {
            attributes.Add((Encoding.UTF8.GetString(name), Encoding.UTF8.GetString(value)));
        }

        Assert.Equal(3, attributes.Count);
        Assert.Equal(("sku", "A-100"), attributes[0]);
        Assert.Equal(("price", "19.99"), attributes[1]);
        Assert.Equal(("inStock", "true"), attributes[2]);

        // Subsequent call on exhausted attributes returns false
        Assert.False(reader.TryReadNextAttribute(ref position, out _, out _));
    }

    [Fact]
    public void TryReadNextAttribute_NoAttributes_ReturnsFalse()
    {
        var xml = "<item />"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read());
        var position = 0;
        Assert.False(reader.TryReadNextAttribute(ref position, out _, out _));
    }

    [Fact]
    public void TryReadNextAttribute_CalledOnNonStartElement_ReturnsFalse()
    {
        var xml = "<root></root>"u8;
        var reader = new Utf8XmlReader(xml);

        Assert.True(reader.Read()); // <root>
        Assert.True(reader.Read()); // </root>
        Assert.Equal(Utf8XmlTokenType.EndElement, reader.TokenType);

        var position = 0;
        Assert.False(reader.TryReadNextAttribute(ref position, out _, out _));
    }

    // ========================================================================
    // 3. Depth & Hierarchy Tracking
    // ========================================================================

    [Fact]
    public void Depth_NestedDocument_TrackedAccuratelyAtEachLevel()
    {
        var xml = """
            <root>
              <child a="1">
                <grandchild/>
                <grandchild2>Content</grandchild2>
              </child>
            </root>
            """u8;

        var reader = new Utf8XmlReader(xml);
        var tokens = new List<(Utf8XmlTokenType Type, string? Name, int Depth)>();

        while (reader.Read())
        {
            var name = reader.TokenType is Utf8XmlTokenType.StartElement or Utf8XmlTokenType.EndElement
                ? Encoding.UTF8.GetString(reader.NameSpan)
                : null;
            tokens.Add((reader.TokenType, name, reader.Depth));
        }

        Assert.Contains((Utf8XmlTokenType.StartElement, "root", 0), tokens);
        Assert.Contains((Utf8XmlTokenType.StartElement, "child", 1), tokens);
        Assert.Contains((Utf8XmlTokenType.StartElement, "grandchild", 2), tokens);
        Assert.Contains((Utf8XmlTokenType.StartElement, "grandchild2", 2), tokens);
        Assert.Contains((Utf8XmlTokenType.Text, null, 3), tokens); // inside grandchild2
        Assert.Contains((Utf8XmlTokenType.EndElement, "grandchild2", 2), tokens);
        Assert.Contains((Utf8XmlTokenType.EndElement, "child", 1), tokens);
        Assert.Contains((Utf8XmlTokenType.EndElement, "root", 0), tokens);
    }

    [Fact]
    public void CurrentState_PreservesAndRestoresDepthAcrossReaders()
    {
        var xmlPart1 = "<root><child>"u8;
        var reader1 = new Utf8XmlReader(xmlPart1, isFinalBlock: false);

        Assert.True(reader1.Read()); // <root> (Depth 0, _depth 1)
        Assert.True(reader1.Read()); // <child> (Depth 1, _depth 2)
        Assert.Equal(2, reader1.CurrentState.Depth);

        var state = reader1.CurrentState;
        var xmlPart2 = "<leaf/></child></root>"u8;
        var reader2 = new Utf8XmlReader(xmlPart2, isFinalBlock: true, state: state);

        Assert.Equal(2, reader2.Depth);
        Assert.True(reader2.Read());
        Assert.Equal(Utf8XmlTokenType.StartElement, reader2.TokenType);
        Assert.Equal("leaf", Encoding.UTF8.GetString(reader2.NameSpan));
        Assert.Equal(2, reader2.Depth);
        Assert.True(reader2.IsEmptyElement);

        Assert.True(reader2.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader2.TokenType);
        Assert.Equal("child", Encoding.UTF8.GetString(reader2.NameSpan));
        Assert.Equal(1, reader2.Depth);

        Assert.True(reader2.Read());
        Assert.Equal(Utf8XmlTokenType.EndElement, reader2.TokenType);
        Assert.Equal("root", Encoding.UTF8.GetString(reader2.NameSpan));
        Assert.Equal(0, reader2.Depth);

        Assert.False(reader2.Read());
    }

    // ========================================================================
    // 4. UTF-8 Characters Handling
    // ========================================================================

    [Fact]
    public void Utf8_InternationalCharacters_HandledCorrectly()
    {
        var xml = """
            <каталог>
              <элемент id="1" 名称="тест" café="crème">
                <данные><![CDATA[特殊字符 «€»]]></данные>
                <!-- Комментарий: 100% utf-8 -->
                日本語のテキスト
              </элемент>
            </каталог>
            """u8;

        var reader = new Utf8XmlReader(xml);

        // <каталог>
        Assert.True(reader.Read());
        Assert.Equal("каталог", Encoding.UTF8.GetString(reader.NameSpan));

        // text (whitespace)
        Assert.True(reader.Read());

        // <элемент>
        Assert.True(reader.Read());
        Assert.Equal("элемент", Encoding.UTF8.GetString(reader.NameSpan));
        Assert.True(reader.TryGetAttribute("名称"u8, out var nameVal));
        Assert.Equal("тест", Encoding.UTF8.GetString(nameVal));
        Assert.True(reader.TryGetAttribute("café"u8, out var cafeVal));
        Assert.Equal("crème", Encoding.UTF8.GetString(cafeVal));

        // text (whitespace)
        Assert.True(reader.Read());

        // <данные>
        Assert.True(reader.Read());
        Assert.Equal("данные", Encoding.UTF8.GetString(reader.NameSpan));

        // CDATA
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.CData, reader.TokenType);
        Assert.Equal("特殊字符 «€»", Encoding.UTF8.GetString(reader.ValueSpan));

        // </данные>
        Assert.True(reader.Read());
        Assert.Equal("данные", Encoding.UTF8.GetString(reader.NameSpan));

        // text (whitespace)
        Assert.True(reader.Read());

        // Comment
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Comment, reader.TokenType);
        Assert.Equal(" Комментарий: 100% utf-8 ", Encoding.UTF8.GetString(reader.ValueSpan));

        // text
        Assert.True(reader.Read());
        Assert.Equal(Utf8XmlTokenType.Text, reader.TokenType);
        Assert.Contains("日本語のテキスト", Encoding.UTF8.GetString(reader.ValueSpan));

        // </элемент>
        Assert.True(reader.Read());
        Assert.Equal("элемент", Encoding.UTF8.GetString(reader.NameSpan));

        // text (whitespace)
        Assert.True(reader.Read());

        // </каталог>
        Assert.True(reader.Read());
        Assert.Equal("каталог", Encoding.UTF8.GetString(reader.NameSpan));

        Assert.False(reader.Read());
    }

    // ========================================================================
    // 5. Streaming & Chunk Boundary Tests
    // ========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(32)]
    public void Read_ArbitraryChunkSizes_ProducesIdenticalTokensAsSingleBlock(int chunkSize)
    {
        var xmlString = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE catalog SYSTEM "catalog.dtd">
            <!-- Header comment -->
            <catalog date="2026-08-29">
              <book id="bk101" price="44.95">
                <author>Gambardella, Matthew</author>
                <title>XML Developer's Guide</title>
                <genre>Computer</genre>
                <description><![CDATA[An <b>in-depth</b> look at XML.]]></description>
              </book>
              <book id="bk102" price="5.95">
                <author>Ralls, Kim</author>
                <title>Midnight Rain</title>
                <genre>Fantasy</genre>
                <empty-tag test="true" />
              </book>
            </catalog>
            """;

        var fullBytes = Encoding.UTF8.GetBytes(xmlString);

        // 1. Collect reference tokens from single block
        var referenceTokens = new List<(Utf8XmlTokenType Type, string Name, string Value, int Depth, bool IsEmpty)>();
        var singleReader = new Utf8XmlReader(fullBytes, isFinalBlock: true);
        while (singleReader.Read())
        {
            referenceTokens.Add((
                singleReader.TokenType,
                Encoding.UTF8.GetString(singleReader.NameSpan),
                Encoding.UTF8.GetString(singleReader.ValueSpan),
                singleReader.Depth,
                singleReader.IsEmptyElement
            ));
        }

        // 2. Stream through chunks
        var chunkTokens = new List<(Utf8XmlTokenType Type, string Name, string Value, int Depth, bool IsEmpty)>();
        var state = default(Utf8XmlReaderState);
        var buffer = new List<byte>();
        var streamOffset = 0;

        while (streamOffset < fullBytes.Length)
        {
            var take = Math.Min(chunkSize, fullBytes.Length - streamOffset);
            buffer.AddRange(fullBytes.AsSpan(streamOffset, take).ToArray());
            streamOffset += take;
            var isFinal = streamOffset == fullBytes.Length;

            var reader = new Utf8XmlReader(buffer.ToArray(), isFinalBlock: isFinal, state: state);
            while (reader.Read())
            {
                chunkTokens.Add((
                    reader.TokenType,
                    Encoding.UTF8.GetString(reader.NameSpan),
                    Encoding.UTF8.GetString(reader.ValueSpan),
                    reader.Depth,
                    reader.IsEmptyElement
                ));
            }

            state = reader.CurrentState;
            var consumed = (int)reader.BytesConsumed;
            buffer.RemoveRange(0, consumed);
        }

        Assert.Equal(referenceTokens.Count, chunkTokens.Count);
        for (var i = 0; i < referenceTokens.Count; i++)
        {
            Assert.Equal(referenceTokens[i], chunkTokens[i]);
        }
    }

    [Fact]
    public void Read_NestedXmlAcrossChunks_TracksAbsoluteStartAndEndPositions()
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

        const int chunkCount = 5;
        var chunkSize = (bytes.Length + chunkCount - 1) / chunkCount;
        var chunks = new List<byte[]>();
        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, bytes.Length - offset);
            chunks.Add(bytes[offset..(offset + length)]);
        }

        Assert.Equal(5, chunks.Count);

        var positions = new List<(int startPos, int endPos)>();
        var buffer = new List<byte>();
        var state = default(Utf8XmlReaderState);
        var globalOffset = 0;
        var currentItemStart = -1;

        for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            buffer.AddRange(chunk);
            var isFinal = chunkIndex == chunks.Count - 1;

            var reader = new Utf8XmlReader(buffer.ToArray(), isFinalBlock: isFinal, state: state);
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

    [Theory]
    [InlineData("<!")]
    [InlineData("<!-")]
    [InlineData("<!--")]
    [InlineData("<![")]
    [InlineData("<![C")]
    [InlineData("<![CD")]
    [InlineData("<![CDA")]
    [InlineData("<![CDAT")]
    [InlineData("<![CDATA")]
    [InlineData("<![CDATA[")]
    [InlineData("<!D")]
    [InlineData("<!DO")]
    [InlineData("<!DOC")]
    public void Read_IncompleteDeclarationPrefixWhenNotFinalBlock_ReturnsFalseWithoutThrowing(string prefix)
    {
        var bytes = Encoding.UTF8.GetBytes(prefix);
        var reader = new Utf8XmlReader(bytes, isFinalBlock: false);

        Assert.False(reader.Read());
        Assert.Equal(0, reader.BytesConsumed);
    }

    [Fact]
    public void Read_IncompleteTokensWhenNotFinalBlock_ReturnsFalseAndDoesNotConsume()
    {
        string[] incompleteSamples =
        [
            "<root",
            "<root id=\"val\"",
            "</root",
            "<!-- comment",
            "<![CDATA[ data",
            "<?xml version=\"1.0\"",
            "<!DOCTYPE html",
            "<!DOCTYPE note [ <!ELEMENT note >"
        ];

        foreach (var sample in incompleteSamples)
        {
            var bytes = Encoding.UTF8.GetBytes(sample);
            var reader = new Utf8XmlReader(bytes, isFinalBlock: false);

            Assert.False(reader.Read());
            Assert.Equal(0, reader.BytesConsumed);
        }
    }

    // ========================================================================
    // 6. Malformed XML & Error Handling
    // ========================================================================

    [Theory]
    [InlineData("<!ENTITY name \"value\">")]
    [InlineData("<!ELEMENT note (to,from)>")]
    [InlineData("<!ATTLIST note id CDATA #REQUIRED>")]
    public void Read_UnsupportedXmlDeclaration_ThrowsInvalidDataException(string xmlString)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            var reader = new Utf8XmlReader(xml);
            reader.Read();
        });
        Assert.Equal("Unsupported XML declaration.", ex.Message);
    }

    [Theory]
    [InlineData("<>", "Empty XML element name.")]
    [InlineData("< >", "Empty XML element name.")]
    [InlineData("<  >", "Empty XML element name.")]
    public void Read_EmptyStartElementName_ThrowsInvalidDataException(string xmlString, string expectedMessage)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            var reader = new Utf8XmlReader(xml);
            reader.Read();
        });
        Assert.Equal(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("<root></>", "Empty XML end element name.")]
    [InlineData("<root></ >", "Empty XML end element name.")]
    [InlineData("<root></  >", "Empty XML end element name.")]
    public void Read_EmptyEndElementName_ThrowsInvalidDataException(string xmlString, string expectedMessage)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            var reader = new Utf8XmlReader(xml);
            Assert.True(reader.Read()); // <root>
            reader.Read();
        });
        Assert.Equal(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("<root></root invalid>", "Malformed XML end element.")]
    [InlineData("<root></root attr=\"1\">", "Malformed XML end element.")]
    [InlineData("<root></root/>", "Malformed XML end element.")]
    public void Read_MalformedEndElement_ThrowsInvalidDataException(string xmlString, string expectedMessage)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            var reader = new Utf8XmlReader(xml);
            Assert.True(reader.Read()); // <root>
            reader.Read();
        });
        Assert.Equal(expectedMessage, ex.Message);
    }

    [Fact]
    public void Read_UnexpectedEndElementAtRoot_ThrowsInvalidDataException()
    {
        var xml = Encoding.UTF8.GetBytes("</root>");
        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            var reader = new Utf8XmlReader(xml);
            reader.Read();
        });
        Assert.Equal("Unexpected XML end element.", ex.Message);
    }

    [Theory]
    [InlineData("<root", "Incomplete XML start element.")]
    [InlineData("<root><child></child><child2", "Incomplete XML start element.")]
    [InlineData("<root></root", "Incomplete XML end element.")]
    [InlineData("<!-- unclosed", "Incomplete XML comment.")]
    [InlineData("<![CDATA[ unclosed", "Incomplete CDATA section.")]
    [InlineData("<?xml unclosed", "Incomplete XML processing instruction.")]
    [InlineData("<!DOCTYPE html", "Incomplete DOCTYPE declaration.")]
    [InlineData("<!DOCTYPE note [ <!ELEMENT note >", "Incomplete DOCTYPE declaration.")]
    public void Read_IncompleteTokensOnFinalBlock_ThrowsInvalidDataException(string xmlString, string expectedMessage)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            var reader = new Utf8XmlReader(xml, isFinalBlock: true);
            while (reader.Read()) { }
        });

        Assert.Equal(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("<root attr>", "Malformed XML attribute.")]
    [InlineData("<root attr   >", "Malformed XML attribute.")]
    [InlineData("<root =\"value\">", "Malformed XML attribute.")]
    [InlineData("<root  =  \"value\">", "Malformed XML attribute.")]
    public void TryGetAttribute_MalformedAttribute_ThrowsInvalidDataException(string xmlString, string expectedMessage)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            var reader = new Utf8XmlReader(xml);
            Assert.True(reader.Read());
            reader.TryGetAttribute("attr"u8, out _);
        });
        Assert.Equal(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("<root attr=123>", "XML attribute value must be quoted.")]
    [InlineData("<root attr=unquoted>", "XML attribute value must be quoted.")]
    public void TryGetAttribute_UnquotedAttributeValue_ThrowsInvalidDataException(string xmlString, string expectedMessage)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            var reader = new Utf8XmlReader(xml);
            Assert.True(reader.Read());
            reader.TryGetAttribute("attr"u8, out _);
        });
        Assert.Equal(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("<root attr>", "Malformed XML attribute.")]
    [InlineData("<root =\"val\">", "Malformed XML attribute.")]
    [InlineData("<root attr=123>", "XML attribute value must be quoted.")]
    public void TryReadNextAttribute_MalformedAttribute_ThrowsInvalidDataException(string xmlString, string expectedMessage)
    {
        var xml = Encoding.UTF8.GetBytes(xmlString);
        var ex = Assert.Throws<InvalidDataException>(() =>
        {
            var reader = new Utf8XmlReader(xml);
            Assert.True(reader.Read());
            var pos = 0;
            reader.TryReadNextAttribute(ref pos, out _, out _);
        });
        Assert.Equal(expectedMessage, ex.Message);
    }
}