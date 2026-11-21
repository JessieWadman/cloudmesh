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
    public void Create_embedsTheCurrentTimestamp()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = Uuid.Create();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Equal(7, Version(id));
        Assert.Equal(0b10, Variant(id));
        // FastClock tracks the wall clock within a small drift; the counter may also run a ms or so ahead.
        Assert.InRange(Timestamp(id), before - 1_000, after + 1_000);
    }

    [Fact]
    public void Create_isStrictlyIncreasing_onOneThread()
    {
        var previous = RfcBytes(Uuid.Create());
        for (var i = 0; i < 200_000; i++)
        {
            var current = RfcBytes(Uuid.Create());
            Assert.True(Compare(previous, current) < 0, $"id #{i} did not sort after its predecessor");
            previous = current;
        }
    }

    [Fact]
    public void Create_isUnique_andOrderedPerThread_underContention()
    {
        const int threads = 8;
        const int perThread = 25_000;

        var sequences = new Guid[threads][];
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            var ids = new Guid[perThread];
            for (var i = 0; i < perThread; i++)
                ids[i] = Uuid.Create();
            sequences[t] = ids;
        });

        foreach (var ids in sequences)
            for (var i = 1; i < ids.Length; i++)
                Assert.True(Compare(RfcBytes(ids[i - 1]), RfcBytes(ids[i])) < 0, "a thread observed ids out of order");

        var all = sequences.SelectMany(s => s).ToArray();
        Assert.Equal(all.Length, new HashSet<Guid>(all).Count);

        // Global order: anything minted after the burst sorts after everything minted during it.
        var last = RfcBytes(Uuid.Create());
        Assert.All(all, id => Assert.True(Compare(RfcBytes(id), last) < 0));
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
