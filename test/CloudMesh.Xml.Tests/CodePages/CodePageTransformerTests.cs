using System.Buffers;
using System.Text;
using CloudMesh.Xml.CodePages;
using Xunit;

namespace CloudMesh.Xml.Tests.CodePages;

public class CodePageTransformerTests
{
    static CodePageTransformerTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CodePageTransformer(null!, Encoding.UTF8));
        Assert.Throws<ArgumentNullException>(() => new CodePageTransformer(Encoding.UTF8, null!));
    }

    [Fact]
    public void Transform_Span_NullDestination_ThrowsArgumentNullException()
    {
        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        Assert.Throws<ArgumentNullException>(() => transformer.Transform(ReadOnlySpan<byte>.Empty, null!));
    }

    [Fact]
    public void Transform_Sequence_NullDestination_ThrowsArgumentNullException()
    {
        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var sequence = new ReadOnlySequence<byte>([1, 2, 3]);
        Assert.Throws<ArgumentNullException>(() => transformer.Transform(sequence, null!));
    }

    [Fact]
    public void Disposed_Operations_ThrowObjectDisposedException()
    {
        var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        transformer.Dispose();

        var writer = new ArrayBufferWriter<byte>();
        Assert.Throws<ObjectDisposedException>(() => transformer.Transform(ReadOnlySpan<byte>.Empty, writer));
        Assert.Throws<ObjectDisposedException>(() => transformer.Transform(new ReadOnlySequence<byte>(new byte[1]), writer));
        Assert.Throws<ObjectDisposedException>(() => transformer.Reset());

        // Dispose should be idempotent
        transformer.Dispose();
    }

    [Theory]
    [InlineData("Hello, World!")]
    [InlineData("Special characters: ä, ö, ü, ß, é, è, ç, ñ, å, ø")]
    [InlineData("Non-Latin scripts: Привет мир! 🚀 🌟 漢字 한국어 العربية")]
    [InlineData("")]
    public void Transform_Utf8ToUtf16_MatchesEncoding(string input)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(input);
        var expectedBytes = Encoding.Unicode.GetBytes(input);

        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();
        transformer.Transform(sourceBytes, writer, flush: true);

        Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData("Hello, World!")]
    [InlineData("Special characters: ä, ö, ü, ß, é, è, ç, ñ, å, ø")]
    [InlineData("Non-Latin scripts: Привет мир! 🚀 🌟 漢字 한국어 العربية")]
    [InlineData("")]
    public void Transform_Utf16ToUtf8_MatchesEncoding(string input)
    {
        var sourceBytes = Encoding.Unicode.GetBytes(input);
        var expectedBytes = Encoding.UTF8.GetBytes(input);

        using var transformer = new CodePageTransformer(Encoding.Unicode, Encoding.UTF8);
        var writer = new ArrayBufferWriter<byte>();
        transformer.Transform(sourceBytes, writer, flush: true);

        Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData("windows-1252", "Hello, café and naïve résumé in Windows-1252! £50 €100")]
    [InlineData("iso-8859-1", "Latin1 test: äöüß éèç ñåø £¥©®")]
    [InlineData("shift_jis", "Shift_JIS 日本語テキスト テスト 123")]
    [InlineData("gb2312", "GB2312 简体中文测试 123")]
    [InlineData("euc-kr", "EUC-KR 한국어 텍스트 테스트 123")]
    [InlineData("utf-32", "UTF-32 test: Hello 🌍 🚀 🌟")]
    public void Transform_VariousCodePagesToUtf8(string codePageName, string text)
    {
        var sourceEncoding = Encoding.GetEncoding(codePageName);
        var sourceBytes = sourceEncoding.GetBytes(text);
        var expectedBytes = Encoding.UTF8.GetBytes(text);

        using var transformer = new CodePageTransformer(sourceEncoding, Encoding.UTF8);
        var writer = new ArrayBufferWriter<byte>();
        transformer.Transform(sourceBytes, writer, flush: true);

        Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Transform_LargePayload_ExceedingInternalCharBuffer()
    {
        // 4096 chars is internal buffer size. We test with 25,000 characters.
        var sb = new StringBuilder();
        for (int i = 0; i < 2500; i++)
        {
            sb.Append($"Line {i:D4}: The quick brown fox jumps over the lazy dog. Äöüßéè 🚀\n");
        }
        var text = sb.ToString();

        var sourceBytes = Encoding.UTF8.GetBytes(text);
        var expectedBytes = Encoding.Unicode.GetBytes(text);

        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();
        transformer.Transform(sourceBytes, writer, flush: true);

        Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Transform_ChunkedStream_SplittingMultiByteChars()
    {
        // Multi-byte UTF-8 characters: '€' is 3 bytes (E2 82 AC), '🚀' is 4 bytes (F0 9F 9A 80)
        var text = "Hello € and 🚀 world!";
        var sourceBytes = Encoding.UTF8.GetBytes(text);
        var expectedBytes = Encoding.Unicode.GetBytes(text);

        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();

        // Feed bytes 1 by 1 or in small chunks of 2 bytes without flush until the end
        for (int i = 0; i < sourceBytes.Length; i += 2)
        {
            var chunk = sourceBytes.AsSpan(i, Math.Min(2, sourceBytes.Length - i));
            var isLast = i + 2 >= sourceBytes.Length;
            transformer.Transform(chunk, writer, flush: isLast);
        }

        Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Transform_ReadOnlySequence_SingleAndMultiSegment()
    {
        var text = "The quick brown fox jumps over the lazy dog! With special chars: ÄÖÜß and emojis 🚀🎉";
        var sourceBytes = Encoding.UTF8.GetBytes(text);
        var expectedBytes = Encoding.Unicode.GetBytes(text);

        // Single segment sequence
        {
            using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
            var sequence = new ReadOnlySequence<byte>(sourceBytes);
            var writer = new ArrayBufferWriter<byte>();
            transformer.Transform(sequence, writer, flush: true);
            Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
        }

        // Multi-segment sequence (split every 3 bytes across arbitrary character boundaries)
        {
            using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
            var sequence = CreateMultiSegmentSequence(sourceBytes, segmentSize: 3);
            var writer = new ArrayBufferWriter<byte>();
            transformer.Transform(sequence, writer, flush: true);
            Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
        }
    }

    [Fact]
    public void Transform_EmptySource_Flush_FlushesPendingState()
    {
        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();

        // Transform empty with flush = false
        transformer.Transform(ReadOnlySpan<byte>.Empty, writer, flush: false);
        Assert.Equal(0, writer.WrittenCount);

        // Transform empty with flush = true
        transformer.Transform(ReadOnlySpan<byte>.Empty, writer, flush: true);
        Assert.Equal(0, writer.WrittenCount);
    }

    [Fact]
    public void Transform_WithSmallBufferWriter_HandlesRepetitiveSmallSpans()
    {
        var text = "Testing destination encoder advance with small buffer increments: ÄÖÜß 🚀";
        var sourceBytes = Encoding.UTF8.GetBytes(text);
        var expectedBytes = Encoding.Unicode.GetBytes(text);

        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var smallWriter = new SmallSpanBufferWriter(spanSize: 5);
        transformer.Transform(sourceBytes, smallWriter, flush: true);

        Assert.Equal(expectedBytes, smallWriter.WrittenBytes);
    }

    [Fact]
    public void Reset_ClearsDecoderAndEncoderState()
    {
        var fullText = "Hello 🚀 World";
        var fullBytes = Encoding.UTF8.GetBytes(fullText);

        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();

        // Feed first byte of 4-byte rocket emoji (which is at index 6)
        var partial = fullBytes.AsSpan(0, 7); // Contains "Hello " and first byte of rocket (0xF0)
        transformer.Transform(partial, writer, flush: false);

        // Reset transformer to abandon partial character
        transformer.Reset();

        writer.Clear();
        var newText = "Fresh start";
        var newBytes = Encoding.UTF8.GetBytes(newText);
        var expectedBytes = Encoding.Unicode.GetBytes(newText);

        transformer.Transform(newBytes, writer, flush: true);

        Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Transform_Sequence_FlushFalseThenFlushTrue()
    {
        var textPart1 = "First part of the text. ";
        var textPart2 = "Second part with emoji 🌍!";
        var fullText = textPart1 + textPart2;

        var bytes1 = Encoding.UTF8.GetBytes(textPart1);
        var bytes2 = Encoding.UTF8.GetBytes(textPart2);
        var expectedBytes = Encoding.Unicode.GetBytes(fullText);

        using var transformer = new CodePageTransformer(Encoding.UTF8, Encoding.Unicode);
        var writer = new ArrayBufferWriter<byte>();

        var seq1 = CreateMultiSegmentSequence(bytes1, segmentSize: 2);
        var seq2 = CreateMultiSegmentSequence(bytes2, segmentSize: 2);

        transformer.Transform(seq1, writer, flush: false);
        transformer.Transform(seq2, writer, flush: true);

        Assert.Equal(expectedBytes, writer.WrittenSpan.ToArray());
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

    private sealed class SmallSpanBufferWriter : IBufferWriter<byte>
    {
        private readonly int _spanSize;
        private readonly List<byte> _buffer = new();
        private byte[] _currentChunk;

        public SmallSpanBufferWriter(int spanSize)
        {
            _spanSize = spanSize;
            _currentChunk = new byte[spanSize];
        }

        public byte[] WrittenBytes => _buffer.ToArray();

        public void Advance(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _buffer.Add(_currentChunk[i]);
            }
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            var size = Math.Max(_spanSize, sizeHint);
            if (_currentChunk.Length < size)
                _currentChunk = new byte[size];
            return _currentChunk;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            var size = Math.Max(_spanSize, sizeHint);
            if (_currentChunk.Length < size)
                _currentChunk = new byte[size];
            return _currentChunk;
        }
    }
}
