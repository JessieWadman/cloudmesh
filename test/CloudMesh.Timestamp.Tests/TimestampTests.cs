using Xunit;

namespace CloudMesh.TimestampTests;

public class TimestampTests
{
    private const long ToleranceMs = 1000;

    [Fact]
    public void Now_isMonotonic()
    {
        var previous = Timestamp.Now;
        for (var i = 0; i < 100_000; i++)
        {
            var current = Timestamp.Now;
            Assert.True(current >= previous);
            previous = current;
        }
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(60000)]
    public void ElapsedMath_isExact(long milliseconds)
    {
        var t = Timestamp.Now;
        Assert.Equal(milliseconds, (t + milliseconds) - t);
    }

    [Fact]
    public void ToUnixTimeMilliseconds_isCloseToSystemClock()
    {
        var value = Timestamp.Now.ToUnixTimeMilliseconds();
        Assert.True(Math.Abs(value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) < ToleranceMs);
    }

    [Fact]
    public void ToExactUnixTimeMilliseconds_isCloseToSystemClock()
    {
        var value = Timestamp.Now.ToExactUnixTimeMilliseconds();
        Assert.True(Math.Abs(value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) < ToleranceMs);
    }

    [Fact]
    public void ToDateTimeOffset_matchesToUnixTimeMilliseconds()
    {
        var t = Timestamp.Now;
        Assert.Equal(t.ToUnixTimeMilliseconds(), t.ToDateTimeOffset().ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Comparisons_areConsistent()
    {
        var earlier = Timestamp.Now;
        var later = earlier + 1;

        Assert.True(earlier < later);
        Assert.True(later > earlier);
        Assert.True(earlier <= later);
        Assert.True(later >= earlier);
        Assert.Equal(0, earlier.CompareTo(earlier));
        Assert.Equal(1L, later - earlier);
    }
}
