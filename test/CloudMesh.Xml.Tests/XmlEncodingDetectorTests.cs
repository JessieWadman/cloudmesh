using System.Buffers;
using System.Text;
using Xunit;

namespace CloudMesh.Xml.Tests;

public class XmlEncodingDetectorTests
{
    // ========================================================================
    // 1. Empty / Default Inputs
    // ========================================================================

    [Fact]
    public void Detect_EmptySpan_ReturnsUtf8()
    {
        var encoding = XmlEncodingDetector.Detect(ReadOnlySpan<byte>.Empty);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_EmptyMemory_ReturnsUtf8()
    {
        var encoding = XmlEncodingDetector.Detect(ReadOnlyMemory<byte>.Empty);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_EmptySequence_ReturnsUtf8()
    {
        var encoding = XmlEncodingDetector.Detect(ReadOnlySequence<byte>.Empty);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    // ========================================================================
    // 2. Byte Order Marks (BOM)
    // ========================================================================

    [Fact]
    public void Detect_Utf8Bom_ReturnsUtf8()
    {
        byte[] data = [0xEF, 0xBB, 0xBF, (byte)'<', (byte)'r', (byte)'o', (byte)'o', (byte)'t', (byte)'>'];
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf16LeBom_ReturnsUnicode()
    {
        byte[] data = [0xFF, 0xFE, 0x3C, 0x00, 0x72, 0x00]; // FF FE < r
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(Encoding.Unicode.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf16BeBom_ReturnsBigEndianUnicode()
    {
        byte[] data = [0xFE, 0xFF, 0x00, 0x3C, 0x00, 0x72]; // FE FF < r
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(Encoding.BigEndianUnicode.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf32LeBom_ReturnsUtf32()
    {
        byte[] data = [0xFF, 0xFE, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00];
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(Encoding.UTF32.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf32BeBom_ReturnsUtf32BigEndian()
    {
        byte[] data = [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x3C];
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(12001, encoding.CodePage); // UTF-32 BE code page
    }

    // ========================================================================
    // 3. Multi-byte Signatures Without BOM
    // ========================================================================

    [Fact]
    public void Detect_Utf32BeWithoutBom_ReturnsUtf32BigEndian()
    {
        byte[] data = [0x00, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00, 0x72];
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(12001, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf32LeWithoutBom_ReturnsUtf32LittleEndian()
    {
        byte[] data = [0x3C, 0x00, 0x00, 0x00, 0x72, 0x00, 0x00, 0x00];
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(Encoding.UTF32.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf16BeWithoutBom_Declaration_ReturnsBigEndianUnicode()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-16BE\"?><root/>";
        var bytes = Encoding.BigEndianUnicode.GetBytes(xml);
        var encoding = XmlEncodingDetector.Detect(bytes);
        Assert.Equal(Encoding.BigEndianUnicode.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf16LeWithoutBom_Declaration_ReturnsUnicode()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><root/>";
        var bytes = Encoding.Unicode.GetBytes(xml);
        var encoding = XmlEncodingDetector.Detect(bytes);
        Assert.Equal(Encoding.Unicode.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf16BeWithoutBom_NoDeclaration_ReturnsBigEndianUnicode()
    {
        byte[] data = [0x00, 0x3C, 0x00, 0x72, 0x00, 0x6F, 0x00, 0x6F, 0x00, 0x74, 0x00, 0x3E]; // <root>
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(Encoding.BigEndianUnicode.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf16LeWithoutBom_NoDeclaration_ReturnsUnicode()
    {
        byte[] data = [0x3C, 0x00, 0x72, 0x00, 0x6F, 0x00, 0x6F, 0x00, 0x74, 0x00, 0x3E, 0x00]; // <root>
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(Encoding.Unicode.CodePage, encoding.CodePage);
    }

    // ========================================================================
    // 4. ASCII-Compatible XML Declarations
    // ========================================================================

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?><root/>", 65001)]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?><root/>", 65001)]
    [InlineData("<?xml version='1.0' encoding='UTF-8'?><root/>", 65001)]
    [InlineData("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><root/>", 28591)]
    [InlineData("<?xml version=\"1.0\" encoding=\"latin1\"?><root/>", 28591)]
    [InlineData("<?xml version=\"1.0\" encoding=\"windows-1252\"?><root/>", 1252)]
    [InlineData("<?xml version=\"1.0\" encoding=\"windows-1251\"?><root/>", 1251)]
    [InlineData("<?xml version=\"1.0\" encoding=\"us-ascii\"?><root/>", 20127)]
    [InlineData("<?xml version=\"1.0\" encoding=\"ascii\"?><root/>", 20127)]
    [InlineData("<?xml version=\"1.0\" encoding=\"Shift_JIS\"?><root/>", 932)]
    [InlineData("<?xml version=\"1.0\" encoding=\"GB2312\"?><root/>", 936)]
    [InlineData("<?xml version=\"1.0\" encoding = 'windows-1252' standalone=\"yes\" ?><root/>", 1252)]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-16\"?><root/>", 1200)]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-16LE\"?><root/>", 1200)]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-16BE\"?><root/>", 1201)]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-32\"?><root/>", 12000)]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-32BE\"?><root/>", 12001)]
    public void Detect_XmlDeclaration_DetectsCorrectEncoding(string xml, int expectedCodePage)
    {
        var bytes = Encoding.Latin1.GetBytes(xml);
        var encoding = XmlEncodingDetector.Detect(bytes);
        Assert.Equal(expectedCodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_XmlDeclarationWithoutEncoding_DefaultsToUtf8()
    {
        var xml = "<?xml version=\"1.0\"?><root/>"u8;
        var encoding = XmlEncodingDetector.Detect(xml);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_XmlWithoutDeclaration_DefaultsToUtf8()
    {
        var xml = "<root><child>value</child></root>"u8;
        var encoding = XmlEncodingDetector.Detect(xml);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_XmlStyleSheetNotXmlDeclaration_DefaultsToUtf8()
    {
        var xml = "<?xml-stylesheet type=\"text/xsl\" href=\"style.xsl\"?><root/>"u8;
        var encoding = XmlEncodingDetector.Detect(xml);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    // ========================================================================
    // 5. ReadOnlySequence Segmented Inputs
    // ========================================================================

    [Fact]
    public void Detect_MultiSegmentSequence_DetectsCorrectEncoding()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"windows-1252\"?><root/>";
        var bytes = Encoding.Latin1.GetBytes(xml);

        // Split into two segments across the encoding attribute
        var part1 = bytes.AsMemory(0, 25);
        var part2 = bytes.AsMemory(25);

        var segment1 = new Segment<byte>(part1);
        var segment2 = segment1.Append(part2);
        var sequence = new ReadOnlySequence<byte>(segment1, 0, segment2, part2.Length);

        var encoding = XmlEncodingDetector.Detect(sequence);
        Assert.Equal(1252, encoding.CodePage);
    }

    private sealed class Segment<T> : ReadOnlySequenceSegment<T>
    {
        public Segment(ReadOnlyMemory<T> memory)
        {
            Memory = memory;
        }

        public Segment<T> Append(ReadOnlyMemory<T> memory)
        {
            var next = new Segment<T>(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = next;
            return next;
        }
    }
}
