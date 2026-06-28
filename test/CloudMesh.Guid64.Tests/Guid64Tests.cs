using Xunit;

namespace CloudMesh.Guid64Tests;

public class Guid64Tests
{
    [Fact]
    public void ToString_default_isThirteenCharCrockford()
    {
        var g = (Guid64)1520779705068019712L;
        var s = g.ToString();
        Assert.Equal(13, s.Length);
        Assert.Equal(g.ToString("B"), s);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Parse_roundtripsToString(long value)
    {
        var g = (Guid64)value;

        Assert.Equal(g, Guid64.Parse(g.ToString()));
        Assert.True(Guid64.TryParse(g.ToString(), out var parsed));
        Assert.Equal(g, parsed);
    }

    [Fact]
    public void NewGuid_isUnique_positive_monotonic_andRoundtrips()
    {
        const int count = 50_000;
        var seen = new HashSet<long>(count);
        var previous = long.MinValue;

        for (var i = 0; i < count; i++)
        {
            Guid64 g = Guid64.NewGuid();
            long value = g;

            Assert.True(value >= 0, "Snowflake ids are non-negative");
            Assert.True(value > previous, "ids are strictly increasing on a single thread");
            Assert.True(seen.Add(value), "ids are unique");
            Assert.Equal(g, Guid64.Parse(g.ToString()));

            previous = value;
        }
    }

    [Fact]
    public void Parse_invalidInput_returnsFalseOrThrows()
    {
        Assert.False(Guid64.TryParse("not valid!", out _));
        Assert.False(Guid64.TryParse((string?)null, out _));
        Assert.Throws<FormatException>(() => Guid64.Parse("???"));
        Assert.Throws<ArgumentNullException>(() => Guid64.Parse((string)null!));
    }

    [Fact]
    public void IParsable_and_ISpanParsable_overloadsWork()
    {
        var g = (Guid64)42L;

        Assert.Equal(g, Guid64.Parse(g.ToString(), provider: null));
        Assert.True(Guid64.TryParse(g.ToString(), provider: null, out var parsed));
        Assert.Equal(g, parsed);
        Assert.True(Guid64.TryParse(g.ToString().AsSpan(), provider: null, out var spanParsed));
        Assert.Equal(g, spanParsed);
    }

    [Fact]
    public void ToString_D_and_X_matchUnderlyingLong()
    {
        var g = (Guid64)1234567890L;
        Assert.Equal("1234567890", g.ToString("D"));
        Assert.Equal(1234567890L.ToString("X"), g.ToString("X"));
    }

    [Fact]
    public void ImplicitConversions_andComparisons()
    {
        Guid64 a = 10L;
        Guid64 b = 20L;

        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(a != b);
        Assert.Equal(10L, (long)a);
        Assert.Equal(0, a.CompareTo((Guid64)10L));
    }

    [Fact]
    public void TryFormat_writesThirteenChars_andRoundtrips()
    {
        var g = (Guid64)1520779705068019712L;
        Span<char> buffer = stackalloc char[13];

        Assert.True(g.TryFormat(buffer, out var written));
        Assert.Equal(13, written);
        Assert.Equal(g, Guid64.Parse(buffer));
    }

    [Fact]
    public void NodeId_roundtrips_andValidatesRange()
    {
        var original = Guid64.NodeId;
        try
        {
            Guid64.NodeId = 512;
            Assert.Equal(512, Guid64.NodeId);
            Assert.Throws<ArgumentOutOfRangeException>(() => Guid64.NodeId = 1024);
            Assert.Throws<ArgumentOutOfRangeException>(() => Guid64.NodeId = -1);
        }
        finally
        {
            Guid64.NodeId = original;
        }
    }
}
