using Xunit;

namespace CloudMesh.Base32Tests;

public class Base32IntegerTests
{
    [Theory]
    [InlineData(0L, "0000000000000")]
    [InlineData(1L, "0000000000001")]
    [InlineData(31L, "000000000000Z")]
    [InlineData(32L, "0000000000010")]
    public void Format_long_producesKnownVectors(long value, string expected)
    {
        Span<char> buffer = stackalloc char[13];
        Base32.Format(value, buffer);
        Assert.Equal(expected, new string(buffer));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(1520779705068019712L)]
    public void Long_roundtrips(long value)
    {
        Span<char> buffer = stackalloc char[13];
        Base32.Format(value, buffer);
        Assert.True(Base32.TryDecodeInt64(buffer, out var decoded));
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void Long_roundtrips_overRandomSample()
    {
        var rng = new Random(20260628);
        Span<char> buffer = stackalloc char[13];
        for (var i = 0; i < 20_000; i++)
        {
            var value = rng.NextInt64(long.MinValue, long.MaxValue);
            Base32.Format(value, buffer);
            Assert.True(Base32.TryDecodeInt64(buffer, out var decoded));
            Assert.Equal(value, decoded);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(123456)]
    public void Int_roundtrips(int value)
    {
        Span<char> buffer = stackalloc char[7];
        Base32.Format(value, buffer);
        Assert.True(Base32.TryDecodeInt32(buffer, out var decoded));
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void Decode_fullRange_maxValue()
        => Assert.Equal(unchecked((long)ulong.MaxValue), Base32.DecodeInt64("FZZZZZZZZZZZZ"));

    [Fact]
    public void Decode_isCaseInsensitive()
    {
        Span<char> buffer = stackalloc char[13];
        Base32.Format(1520779705068019712L, buffer);
        var encoded = new string(buffer);

        Assert.True(Base32.TryDecodeInt64(encoded.ToLowerInvariant(), out var decoded));
        Assert.Equal(1520779705068019712L, decoded);
    }

    [Theory]
    [InlineData("O", 0L)]    // O is read as 0
    [InlineData("I", 1L)]    // I is read as 1
    [InlineData("L", 1L)]    // L is read as 1
    [InlineData("1-0", 32L)] // '-' is ignored, so "10" == 1 * 32
    public void Decode_appliesCrockfordLeniency(string text, long expected)
    {
        Assert.True(Base32.TryDecodeInt64(text, out var decoded));
        Assert.Equal(expected, decoded);
    }

    [Theory]
    [InlineData("")]               // nothing to decode
    [InlineData("---")]            // only separators
    [InlineData("U")]              // U is intentionally not a symbol
    [InlineData("!")]              // junk
    [InlineData("ZZZZZZZZZZZZZZ")] // 14 symbols overflow 64 bits
    [InlineData("G000000000000")]  // 13 symbols but the leading digit overflows 64 bits
    public void Decode_rejectsInvalidInput(string text)
    {
        Assert.False(Base32.TryDecodeInt64(text, out var value));
        Assert.Equal(0L, value);
        Assert.Throws<FormatException>(() => Base32.DecodeInt64(text));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(5, 8)]
    [InlineData(8, 13)]
    public void GetBase32CharCount_isCorrect(int byteLength, int expectedChars)
        => Assert.Equal(expectedChars, Base32.GetBase32CharCount(byteLength));
}
