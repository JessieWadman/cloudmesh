using System.Collections.Concurrent;
using Xunit;

namespace CloudMesh.TimestampTests;

// FastClock deliberately trades strict monotonicity for accuracy (it re-anchors to the system clock), so
// these assert wall-clock tracking and the lock-free re-anchor — not monotonicity.
public class FastClockTests
{
    private const long ToleranceMs = 1000;

    [Fact]
    public void UnixTimeMillisecondsNow_tracksSystemClock()
    {
        var value = FastClock.UnixTimeMillisecondsNow();
        Assert.True(Math.Abs(value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) < ToleranceMs);
    }

    [Fact]
    public void DateTimeOffsetNow_tracksSystemClock()
    {
        var value = FastClock.DateTimeOffsetNow();
        Assert.True(Math.Abs((value - DateTimeOffset.UtcNow).TotalMilliseconds) < ToleranceMs);
    }

    [Fact]
    public void AdjustInterval_roundtrips()
    {
        var original = FastClock.AdjustInterval;
        try
        {
            FastClock.AdjustInterval = TimeSpan.FromSeconds(2);
            Assert.True(Math.Abs(FastClock.AdjustInterval.TotalSeconds - 2) < 0.01);
        }
        finally
        {
            FastClock.AdjustInterval = original;
        }
    }

    [Fact]
    public void ConcurrentReads_whileReanchoring_stayConsistent()
    {
        // Re-anchor on (almost) every call to hammer the lock-free origin swap, then verify no torn /
        // garbage values escape — every reading must still be sane relative to the system clock.
        var original = FastClock.AdjustInterval;
        try
        {
            FastClock.AdjustInterval = TimeSpan.Zero;

            var results = new ConcurrentBag<long>();
            Parallel.For(0, 8, _ =>
            {
                for (var i = 0; i < 50_000; i++)
                    results.Add(FastClock.UnixTimeMillisecondsNow());
            });

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.Equal(8 * 50_000, results.Count);
            Assert.All(results, ms => Assert.True(
                Math.Abs(ms - nowMs) < 5_000,
                $"value {ms} is wildly off from {nowMs} — the origin pair was likely torn"));
        }
        finally
        {
            FastClock.AdjustInterval = original;
        }
    }
}
