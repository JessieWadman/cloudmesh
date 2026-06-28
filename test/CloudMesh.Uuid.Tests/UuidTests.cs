using System.Collections.Concurrent;
using Xunit;

namespace CloudMesh.UuidTests;

public class UuidTests
{
    // RFC 9562 byte order (big-endian): bytes 0-5 = 48-bit timestamp, high nibble of byte 6 = version,
    // top two bits of byte 8 = variant.
    private static byte[] RfcBytes(Guid value)
    {
        var bytes = new byte[16];
        Assert.True(value.TryWriteBytes(bytes, bigEndian: true, out var written));
        Assert.Equal(16, written);
        return bytes;
    }

    private static int Version(Guid value) => RfcBytes(value)[6] >> 4;

    private static int Variant(Guid value) => RfcBytes(value)[8] >> 6;

    private static long Timestamp(Guid value)
    {
        var bytes = RfcBytes(value);
        long ms = 0;
        for (var i = 0; i < 6; i++)
            ms = (ms << 8) | bytes[i];
        return ms;
    }

    [Fact]
    public void Create_setsVersion7_andVariant10()
    {
        for (var i = 0; i < 1_000; i++)
        {
            var id = Uuid.Create();
            Assert.Equal(7, Version(id));
            Assert.Equal(0b10, Variant(id));
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(1_700_000_000_000L)]
    [InlineData((1L << 48) - 1)] // largest value that fits the 48-bit timestamp field
    public void Next_embedsTheTimestamp(long unixMs)
    {
        var id = Uuid.Next(unixMs);
        Assert.Equal(unixMs, Timestamp(id));
        Assert.Equal(7, Version(id));
        Assert.Equal(0b10, Variant(id));
    }

    [Fact]
    public void Next_negativeTimestamp_throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Uuid.Next(-1L));

    [Fact]
    public void Next_dateTimeOffset_matchesUnixMilliseconds()
    {
        var when = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_123_456L);
        Assert.Equal(when.ToUnixTimeMilliseconds(), Timestamp(Uuid.Next(when)));
    }

    [Fact]
    public void Next_sameTimestamp_producesDifferentRandomBits()
    {
        // Same millisecond -> values differ (random rand_a/rand_b), but the timestamp prefix is identical.
        var a = Uuid.Next(1_700_000_000_000L);
        var b = Uuid.Next(1_700_000_000_000L);
        Assert.NotEqual(a, b);
        Assert.Equal(Timestamp(a), Timestamp(b));
    }

    [Fact]
    public void Next_laterTimestamp_sortsAfterEarlier()
    {
        var earlier = RfcBytes(Uuid.Next(1_700_000_000_000L));
        var later = RfcBytes(Uuid.Next(1_700_000_000_001L));
        Assert.True(Compare(earlier, later) < 0, "a later timestamp must sort after an earlier one");
    }

    [Fact]
    public void Create_isUnique_andThreadSafe()
    {
        // Exercises the Random.Shared fix: a shared, non-thread-safe Random would tear under this load
        // and emit duplicate / all-zero random bits, producing collisions.
        const int threads = 8;
        const int perThread = 25_000;

        var produced = new ConcurrentBag<Guid>();
        Parallel.For(0, threads, _ =>
        {
            for (var i = 0; i < perThread; i++)
                produced.Add(Uuid.Create());
        });

        var all = produced.ToArray();
        Assert.Equal(threads * perThread, all.Length);
        Assert.Equal(all.Length, new HashSet<Guid>(all).Count); // every id is distinct
    }

    private static int Compare(byte[] left, byte[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var diff = left[i].CompareTo(right[i]);
            if (diff != 0)
                return diff;
        }

        return 0;
    }
}
