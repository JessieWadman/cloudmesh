using Xunit;

namespace CloudMesh.TimestampTests;

public class HighResolutionTimestampTests
{
    private const long ToleranceMs = 1000;

    [Fact]
    public void Now_isMonotonic()
    {
        var previous = HighResolutionTimestamp.Now;
        for (var i = 0; i < 100_000; i++)
        {
            var current = HighResolutionTimestamp.Now;
            Assert.True(current >= previous);
            previous = current;
        }
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(60000)]
    public void ElapsedMath_roundtripsWholeSeconds(long milliseconds)
    {
        // Whole-second deltas convert ms -> stopwatch ticks -> ms losslessly for any integer frequency.
        var t = HighResolutionTimestamp.Now;
        Assert.Equal(milliseconds, (t + milliseconds) - t);
    }

    [Fact]
    public void ToUnixTimeMilliseconds_isCloseToSystemClock()
    {
        var value = HighResolutionTimestamp.Now.ToUnixTimeMilliseconds();
        Assert.True(Math.Abs(value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) < ToleranceMs);
    }

    [Fact]
    public void ToExactUnixTimeMilliseconds_isCloseToSystemClock()
    {
        var value = HighResolutionTimestamp.Now.ToExactUnixTimeMilliseconds();
        Assert.True(Math.Abs(value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) < ToleranceMs);
    }

    [Fact]
    public void ToDateTimeOffset_matchesToUnixTimeMilliseconds()
    {
        var t = HighResolutionTimestamp.Now;
        Assert.Equal(t.ToUnixTimeMilliseconds(), t.ToDateTimeOffset().ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Comparisons_areConsistent()
    {
        var a = HighResolutionTimestamp.Now;
        var b = a + 1000;

        Assert.True(a < b);
        Assert.True(b > a);
        Assert.Equal(0, a.CompareTo(a));
    }
}
