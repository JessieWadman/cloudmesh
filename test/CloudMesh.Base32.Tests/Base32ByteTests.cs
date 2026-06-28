using System.Text;
using Xunit;

namespace CloudMesh.Base32Tests;

public class Base32ByteTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(100)]
    public void Bytes_roundtrip_charSource(int length)
    {
        var data = RandomBytes(length, seed: length + 1);

        var chars = new char[Base32.GetBase32CharCount(length)];
        Base32.Format(data, chars);

        var decoded = new byte[Base32.GetMaxByteCount(chars.Length)];
        Assert.True(Base32.TryDecode(chars, decoded, out var written));
        Assert.Equal(length, written);
        Assert.Equal(data, decoded.AsSpan(0, written).ToArray());
    }

    [Fact]
    public void Bytes_roundtrip_utf8ByteSource()
    {
        var data = Encoding.UTF8.GetBytes("Hello, Crockford Base32!");

        var chars = new char[Base32.GetBase32CharCount(data.Length)];
        Base32.Format(data, chars);
        var asciiBase32 = Encoding.ASCII.GetBytes(chars); // the Base32 text itself, as bytes

        var decoded = new byte[Base32.GetMaxByteCount(asciiBase32.Length)];
        Assert.True(Base32.TryDecode(asciiBase32, decoded, out var written));
        Assert.Equal(data, decoded.AsSpan(0, written).ToArray());
    }

    [Fact]
    public void Bytes_roundtrip_charSequence_acrossSegments()
    {
        var data = RandomBytes(200, seed: 7);
        var chars = new char[Base32.GetBase32CharCount(data.Length)];
        Base32.Format(data, chars);

        var sequence = SequenceFactory.Create(new string(chars), segmentSize: 7);
        var decoded = new byte[Base32.GetMaxByteCount(chars.Length)];

        Assert.True(Base32.TryDecode(sequence, decoded, out var written));
        Assert.Equal(data, decoded.AsSpan(0, written).ToArray());
    }

    [Fact]
    public void Bytes_roundtrip_byteSequence_acrossSegments()
    {
        var data = RandomBytes(200, seed: 9);
        var chars = new char[Base32.GetBase32CharCount(data.Length)];
        Base32.Format(data, chars);
        var asciiBase32 = Encoding.ASCII.GetBytes(chars);

        var sequence = SequenceFactory.Create((ReadOnlyMemory<byte>)asciiBase32, segmentSize: 11);
        var decoded = new byte[Base32.GetMaxByteCount(asciiBase32.Length)];

        Assert.True(Base32.TryDecode(sequence, decoded, out var written));
        Assert.Equal(data, decoded.AsSpan(0, written).ToArray());
    }

    [Fact]
    public void Decode_destinationTooSmall_failsAndThrows()
    {
        var data = RandomBytes(10, seed: 3);
        var chars = new char[Base32.GetBase32CharCount(data.Length)];
        Base32.Format(data, chars);

        var tooSmall = new byte[3];
        Assert.False(Base32.TryDecode(chars, tooSmall, out var written));
        Assert.Equal(0, written);
        Assert.Throws<ArgumentException>(() => Base32.Decode(chars, tooSmall));
    }

    [Fact]
    public void Decode_invalidSymbol_throwsFormatException()
    {
        var destination = new byte[8];
        Assert.Throws<FormatException>(() => Base32.Decode("ABCU", destination)); // U is not a symbol
    }

    [Fact]
    public void Decode_empty_yieldsZeroBytes()
    {
        Assert.True(Base32.TryDecode(ReadOnlySpan<char>.Empty, Span<byte>.Empty, out var written));
        Assert.Equal(0, written);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(8, 5)]
    [InlineData(13, 8)]
    public void GetMaxByteCount_isCorrect(int charCount, int expectedBytes)
        => Assert.Equal(expectedBytes, Base32.GetMaxByteCount(charCount));

    private static byte[] RandomBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }
}
