using System.Buffers;
using System.Text;
using CloudMesh.Xml.CodePages;
using Xunit;

namespace CloudMesh.Xml.Tests.CodePages;

public class CodePageTransformerAdversarialTests
{
    static CodePageTransformerAdversarialTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ========================================================================
    // 1. Splitting Multi-Byte & Surrogate Sequences Across Chunks
    // ========================================================================

    [Fact]
    public void Transform_Utf8FourByteEmoji_SplitAcrossChunks_DecodesCorrectly()
    {
        // 🚀 = F0 9F 9A 80 (4 bytes in UTF-8)
        var emoji = "🚀";
        var bytes = Encoding.UTF8.GetBytes(emoji);
        Assert.Equal(4, bytes.Length);

        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();

        // Feed byte by byte with flush: false
        transformer.Transform(bytes.AsSpan(0, 1), writer, flush: false);
        transformer.Transform(bytes.AsSpan(1, 1), writer, flush: false);
        transformer.Transform(bytes.AsSpan(2, 1), writer, flush: false);
        transformer.Transform(bytes.AsSpan(3, 1), writer, flush: true);

        var result = Encoding.Unicode.GetString(writer.WrittenSpan);
        Assert.Equal(emoji, result);
    }

    [Theory]
    [InlineData("shift_jis", "日本語テキストテスト")]
    [InlineData("gb2312", "简体中文测试字符串")]
    [InlineData("euc-kr", "한국어텍스트테스트")]
    public void Transform_DoubleByteEncodings_SplitAcrossChunkBoundaries_DecodesCorrectly(string encodingName, string text)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var sourceBytes = encoding.GetBytes(text);

        using var transformer = new CodePageTransformer(encoding, Encoding.UTF8);
        var writer = new ArrayBufferWriter<byte>();

        // Feed 1 byte at a time to force split across every character boundary
        for (int i = 0; i < sourceBytes.Length; i++)
        {
            bool isLast = i == sourceBytes.Length - 1;
            transformer.Transform(sourceBytes.AsSpan(i, 1), writer, flush: isLast);
        }

        var result = Encoding.UTF8.GetString(writer.WrittenSpan);
        Assert.Equal(text, result);
    }

    [Fact]
    public void Transform_Utf16SurrogatePairs_SplitAcrossChunks_DecodesToUtf8()
    {
        // "🌟🌍🚀🎉" consists of 4 surrogate pairs in UTF-16 (16 bytes total)
        var emojiString = "🌟🌍🚀🎉";
        var utf16Bytes = Encoding.Unicode.GetBytes(emojiString);
        Assert.Equal(16, utf16Bytes.Length);

        using var transformer = new CodePageTransformer(Encoding.Unicode, Encoding.UTF8);
        var writer = new ArrayBufferWriter<byte>();

        // Feed in 1-byte chunks to split both within high surrogate, between surrogates, and within low surrogate
        for (int i = 0; i < utf16Bytes.Length; i++)
        {
            bool isLast = i == utf16Bytes.Length - 1;
            transformer.Transform(utf16Bytes.AsSpan(i, 1), writer, flush: isLast);
        }

        var result = Encoding.UTF8.GetString(writer.WrittenSpan);
        Assert.Equal(emojiString, result);
    }

    [Fact]
    public void Transform_Utf32CodePoints_SplitAcross1ByteChunks_DecodesToUtf8()
    {
        var emojiString = "Hello 🌍🚀";
        var utf32Bytes = Encoding.UTF32.GetBytes(emojiString);

        using var transformer = new CodePageTransformer(Encoding.UTF32, Encoding.UTF8);
        var writer = new ArrayBufferWriter<byte>();

        for (int i = 0; i < utf32Bytes.Length; i++)
        {
            bool isLast = i == utf32Bytes.Length - 1;
            transformer.Transform(utf32Bytes.AsSpan(i, 1), writer, flush: isLast);
        }

        var result = Encoding.UTF8.GetString(writer.WrittenSpan);
        Assert.Equal(emojiString, result);
    }

    // ========================================================================
    // 2. ReadOnlySequence With 1-Byte Segments
    // ========================================================================

    [Fact]
    public void Transform_ReadOnlySequence_OneByteSegments_TranscodesAccurately()
    {
        var text = "Advanced character set test: café, résumé, Привет, 日本語, 🚀";
        var sourceBytes = Encoding.UTF8.GetBytes(text);
        var sequence = TestSequenceFactory.Create(sourceBytes, segmentSize: 1);

        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();

        transformer.Transform(sequence, writer, flush: true);

        var result = Encoding.Unicode.GetString(writer.WrittenSpan);
        Assert.Equal(text, result);
    }

    // ========================================================================
    // 3. Reset and Reuse State Verification
    // ========================================================================

    [Fact]
    public void Reset_ClearsDecoderAndEncoderState_AllowsSubsequentCleanTransformations()
    {
        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();

        // Feed partial bytes of a 4-byte emoji (F0 9F) without flushing
        byte[] partialEmoji = [0xF0, 0x9F];
        transformer.Transform(partialEmoji, writer, flush: false);

        // Reset the transformer
        transformer.Reset();
        writer.Clear();

        // Now transform clean text
        var text = "Fresh clean text";
        transformer.Transform(Encoding.UTF8.GetBytes(text), writer, flush: true);

        var result = Encoding.Unicode.GetString(writer.WrittenSpan);
        Assert.Equal(text, result);
    }

    // ========================================================================
    // 4. Stress Testing: 1MB Payloads & Multi-Threading
    // ========================================================================

    [Fact]
    public void Transform_MegaBytePayload_TranscodesAccurately()
    {
        var text = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. 🚀 🌟 1234567890! ", 20_000));
        var sourceBytes = Encoding.UTF8.GetBytes(text);
        Assert.True(sourceBytes.Length > 1_000_000);

        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();

        transformer.Transform(sourceBytes, writer, flush: true);

        var result = Encoding.Unicode.GetString(writer.WrittenSpan);
        Assert.Equal(text, result);
    }

    [Fact]
    public void Transform_ConcurrentInstances_DoNotInterfere()
    {
        const int threadCount = 10;
        const int iterations = 50;

        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < iterations; i++)
            {
                var input = $"Thread test string {i}: 日本語 & café & 🚀";
                using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
                var writer = new ArrayBufferWriter<byte>();

                transformer.Transform(Encoding.UTF8.GetBytes(input), writer, flush: true);
                var output = Encoding.Unicode.GetString(writer.WrittenSpan);

                Assert.Equal(input, output);
            }
        });
    }

    // ========================================================================
    // 5. Empty & Consecutive Flush Operations
    // ========================================================================

    [Fact]
    public void Transform_ConsecutiveEmptyFlushes_ProducesEmptyOutput()
    {
        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();

        transformer.Transform(ReadOnlySpan<byte>.Empty, writer, flush: true);
        Assert.Equal(0, writer.WrittenCount);

        transformer.Transform(ReadOnlySpan<byte>.Empty, writer, flush: true);
        Assert.Equal(0, writer.WrittenCount);
    }
}
