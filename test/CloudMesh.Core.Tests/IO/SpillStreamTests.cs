using System.Text;
using CloudMesh.IO;

namespace CloudMesh.Core.Tests.IO;

public class SpillStreamTests
{
    [Fact]
    public async Task Small_payload_stays_in_memory_and_round_trips()
    {
        var data = Encoding.UTF8.GetBytes("small enough to stay entirely in the resident page set");
        await using var stream = new SpillStream();

        await stream.WriteAsync(data);

        Assert.False(stream.SpilledToDisk); // never touched disk
        Assert.Equal(data.Length, stream.Length);

        stream.Position = 0;
        Assert.Equal(data, await ReadAll(stream, data.Length));
    }

    [Fact]
    public async Task Large_payload_spills_to_disk_and_round_trips()
    {
        // Tiny pages + a tiny resident budget force eviction to the temp file.
        var data = new byte[64 * 1024];
        new Random(42).NextBytes(data);
        await using var stream = new SpillStream(pageSize: 4096, maxResidentPages: 2);

        await stream.WriteAsync(data);

        Assert.True(stream.SpilledToDisk);
        Assert.Equal(data.Length, stream.Length);

        stream.Position = 0;
        Assert.Equal(data, await ReadAll(stream, data.Length));
    }

    [Fact]
    public async Task Random_access_after_spill_reads_the_right_bytes()
    {
        var data = new byte[64 * 1024];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);
        await using var stream = new SpillStream(pageSize: 4096, maxResidentPages: 2);
        await stream.WriteAsync(data);

        // Jump around out of order — every page must fault back in with the correct bytes.
        foreach (var offset in new[] { 60_000, 1_000, 40_000, 4_095, 4_096, 0 })
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[4];
            var n = await stream.ReadAsync(buffer);
            Assert.Equal(4, n);
            for (var k = 0; k < 4; k++)
                Assert.Equal((byte)((offset + k) % 251), buffer[k]);
        }
    }

    [Fact]
    public async Task Hot_page_reread_is_a_pure_memory_operation()
    {
        // Write two pages but keep both resident: re-reading never has to hit disk.
        var data = new byte[8192];
        new Random(7).NextBytes(data);
        await using var stream = new SpillStream(pageSize: 4096, maxResidentPages: 4);
        await stream.WriteAsync(data);

        for (var pass = 0; pass < 5; pass++)
        {
            stream.Position = 0;
            Assert.Equal(data, await ReadAll(stream, data.Length));
        }

        Assert.False(stream.SpilledToDisk); // fit in the resident set the whole time
    }

    [Fact]
    public async Task Seeking_past_the_end_then_writing_reads_the_gap_as_zeros()
    {
        await using var stream = new SpillStream(pageSize: 4096, maxResidentPages: 2);

        stream.Seek(10_000, SeekOrigin.Begin); // leave a hole across page boundaries
        await stream.WriteAsync(new byte[] { 1, 2, 3, 4 });

        Assert.Equal(10_004, stream.Length);

        stream.Position = 0;
        var all = await ReadAll(stream, 10_004);
        Assert.All(all.AsSpan(0, 10_000).ToArray(), b => Assert.Equal(0, b)); // the gap is zero-filled
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, all.AsSpan(10_000, 4).ToArray());
    }

    [Fact]
    public async Task Overwriting_an_evicted_page_persists_the_update()
    {
        var data = new byte[16 * 1024];
        await using var stream = new SpillStream(pageSize: 4096, maxResidentPages: 2);
        await stream.WriteAsync(data); // four pages, only two resident → early pages evicted to disk

        // Go back and overwrite the very first page, forcing it to fault in, mutate, and re-persist.
        stream.Position = 0;
        var patch = Enumerable.Range(0, 4096).Select(i => (byte)(i % 97 + 1)).ToArray();
        await stream.WriteAsync(patch);

        stream.Position = 0;
        Assert.Equal(patch, await ReadAll(stream, patch.Length));
    }

    private static async Task<byte[]> ReadAll(Stream stream, int length)
    {
        var read = new byte[length];
        var offset = 0;
        int n;
        while (offset < length && (n = await stream.ReadAsync(read.AsMemory(offset))) > 0)
            offset += n;
        return read;
    }
}
