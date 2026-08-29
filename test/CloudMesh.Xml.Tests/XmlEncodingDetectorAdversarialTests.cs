using System.Buffers;
using System.Text;
using Xunit;

namespace CloudMesh.Xml.Tests;

public class XmlEncodingDetectorAdversarialTests
{
    static XmlEncodingDetectorAdversarialTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ========================================================================
    // 1. Corrupted & Truncated BOMs
    // ========================================================================

    [Theory]
    [InlineData(new byte[] { 0xEF })] // 1 byte UTF-8 BOM
    [InlineData(new byte[] { 0xEF, 0xBB })] // 2 bytes UTF-8 BOM
    [InlineData(new byte[] { 0xFE })] // 1 byte UTF-16 BE BOM
    [InlineData(new byte[] { 0xFF })] // 1 byte UTF-16 LE BOM
    [InlineData(new byte[] { 0x00 })] // 1 byte null
    [InlineData(new byte[] { 0x00, 0x00 })] // 2 bytes null
    [InlineData(new byte[] { 0x00, 0x00, 0xFE })] // 3 bytes UTF-32 BE BOM
    [InlineData(new byte[] { 0xAA, 0xBB, 0xCC })]
    public void Detect_TruncatedBOM_FallsBackToUtf8Gracefully(byte[] truncatedBom)
    {
        var encoding = XmlEncodingDetector.Detect(truncatedBom);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_Utf16LeBom_WithTrailingZero_DetectsAsUnicode()
    {
        // 3 bytes starting with FF FE matches UTF-16 LE BOM before 4-byte UTF-32 check
        byte[] data = [0xFF, 0xFE, 0x00];
        var encoding = XmlEncodingDetector.Detect(data);
        Assert.Equal(Encoding.Unicode.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_RandomGarbageBytes_FallsBackToUtf8()
    {
        byte[] garbage = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22];
        var encoding = XmlEncodingDetector.Detect(garbage);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    // ========================================================================
    // 2. Malformed / Corrupted XML Declarations
    // ========================================================================

    [Theory]
    [InlineData("<?xml")]
    [InlineData("<?xml ")]
    [InlineData("<?xml version=\"1.0\"")]
    [InlineData("<?xml version=\"1.0\"?")]
    [InlineData("<?xml version=\"1.0\"?>")] // No encoding specified -> UTF-8 default
    [InlineData("<?xml encoding=\"\"?>")] // Empty encoding -> UTF-8 default
    [InlineData("<?xml encoding=\"   \"?>")] // Whitespace encoding -> UTF-8 default
    [InlineData("<?xml version=\"1.0\" encoding=>")] // Malformed
    [InlineData("<?xml version=\"1.0\" encoding=\"unclosed")] // Unclosed quote
    [InlineData("<?xml version=\"1.0\" encoding='unclosed")] // Unclosed single quote
    [InlineData("<?xml other=\"1.0\"?>")] // Unknown attribute
    public void Detect_MalformedDeclaration_FallsBackToUtf8(string declaration)
    {
        var bytes = Encoding.UTF8.GetBytes(declaration);
        var encoding = XmlEncodingDetector.Detect(bytes);
        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    [Fact]
    public void Detect_NonExistentEncodingName_ThrowsArgumentException()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"unknown-encoding-xyz-12345\"?><root/>";
        var bytes = Encoding.UTF8.GetBytes(xml);

        Assert.Throws<ArgumentException>(() => XmlEncodingDetector.Detect(bytes));
    }

    // ========================================================================
    // 3. Declaration Syntax Variations & Edge Cases
    // ========================================================================

    [Theory]
    [InlineData("<?xml version='1.0' encoding='windows-1252'?>", 1252)]
    [InlineData("<?xml version=\"1.0\" encoding=\"windows-1252\"?>", 1252)]
    [InlineData("<?xml encoding=\"windows-1252\" version=\"1.0\"?>", 1252)]
    [InlineData("<?xml standalone=\"yes\" version=\"1.0\" encoding=\"windows-1252\"?>", 1252)]
    [InlineData("<?xml version=\"1.0\" standalone=\"yes\" encoding=\"windows-1252\"?>", 1252)]
    [InlineData("<?xml\t\r\n version=\"1.0\"\t\r\n encoding=\"windows-1252\"\t\r\n ?>", 1252)]
    [InlineData("<?xml version=\"1.0\"encoding=\"windows-1252\"?>", 1252)]
    [InlineData("<?xml version=\"1.0\" encoding = \"windows-1252\" ?>", 1252)]
    [InlineData("<?xml version=\"1.0\" encoding = 'windows-1252' ?>", 1252)]
    [InlineData("<?xml version=\"1.0\" encoding=\"shift_jis\"?>", 932)]
    [InlineData("<?xml version=\"1.0\" encoding=\"gb2312\"?>", 936)]
    [InlineData("<?xml version=\"1.0\" encoding=\"euc-kr\"?>", 51949)]
    [InlineData("<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>", 28591)]
    [InlineData("<?xml version=\"1.0\" encoding=\"latin1\"?>", 28591)]
    [InlineData("<?xml version=\"1.0\" encoding=\"us-ascii\"?>", 20127)]
    [InlineData("<?xml version=\"1.0\" encoding=\"ascii\"?>", 20127)]
    public void Detect_AsciiCompatibleDeclarationVariations_ResolvesExpectedCodePage(string declaration, int expectedCodePage)
    {
        var xml = declaration + "<root/>";
        var bytes = Encoding.ASCII.GetBytes(xml);
        var encoding = XmlEncodingDetector.Detect(bytes);
        Assert.Equal(expectedCodePage, encoding.CodePage);
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", false, 1200)]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-16le\"?>", false, 1200)]
    [InlineData("<?xml version=\"1.0\" encoding=\"UCS-2\"?>", false, 1200)]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-16BE\"?>", true, 1201)]
    public void Detect_Utf16DeclarationsWithoutBom_ResolvesExpectedCodePage(string declaration, bool isBigEndian, int expectedCodePage)
    {
        var enc = isBigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        var bytes = enc.GetBytes(declaration + "<root/>");
        var detected = XmlEncodingDetector.Detect(bytes);
        Assert.Equal(expectedCodePage, detected.CodePage);
    }

    // ========================================================================
    // 4. Multi-Segment ReadOnlySequence Adversarial Slicing
    // ========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void Detect_MultiSegmentSequence_ResolvesCorrectlyAcrossSegmentBoundaries(int segmentSize)
    {
        // 1. UTF-8 BOM sequence
        byte[] utf8BomXml = [0xEF, 0xBB, 0xBF, (byte)'<', (byte)'r', (byte)'o', (byte)'o', (byte)'t', (byte)'/', (byte)'>'];
        var seq1 = TestSequenceFactory.Create(utf8BomXml, segmentSize);
        Assert.Equal(Encoding.UTF8.CodePage, XmlEncodingDetector.Detect(seq1).CodePage);

        // 2. UTF-16 LE BOM sequence
        byte[] utf16LeBomXml = [0xFF, 0xFE, 0x3C, 0x00, 0x72, 0x00, 0x2F, 0x00, 0x3E, 0x00];
        var seq2 = TestSequenceFactory.Create(utf16LeBomXml, segmentSize);
        Assert.Equal(Encoding.Unicode.CodePage, XmlEncodingDetector.Detect(seq2).CodePage);

        // 3. UTF-16 BE BOM sequence
        byte[] utf16BeBomXml = [0xFE, 0xFF, 0x00, 0x3C, 0x00, 0x72, 0x00, 0x2F, 0x00, 0x3E];
        var seq3 = TestSequenceFactory.Create(utf16BeBomXml, segmentSize);
        Assert.Equal(Encoding.BigEndianUnicode.CodePage, XmlEncodingDetector.Detect(seq3).CodePage);

        // 4. UTF-32 LE BOM sequence
        byte[] utf32LeBomXml = [0xFF, 0xFE, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00];
        var seq4 = TestSequenceFactory.Create(utf32LeBomXml, segmentSize);
        Assert.Equal(Encoding.UTF32.CodePage, XmlEncodingDetector.Detect(seq4).CodePage);

        // 5. UTF-32 BE BOM sequence
        byte[] utf32BeBomXml = [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x3C];
        var seq5 = TestSequenceFactory.Create(utf32BeBomXml, segmentSize);
        Assert.Equal(12001, XmlEncodingDetector.Detect(seq5).CodePage);

        // 6. Non-BOM declaration in multi-segment sequence
        var xmlDecl = "<?xml version=\"1.0\" encoding=\"windows-1252\"?><catalog><item/></catalog>";
        var seq6 = TestSequenceFactory.Create(Encoding.ASCII.GetBytes(xmlDecl), segmentSize);
        Assert.Equal(1252, XmlEncodingDetector.Detect(seq6).CodePage);
    }
}
