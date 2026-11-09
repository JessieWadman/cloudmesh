using System.Text;
using CloudMesh.IO;

namespace CloudMesh.Core.Tests;

/// <summary>
/// A stream that dribbles its bytes out in caller-controlled chunk sizes on every <c>ReadAsync</c> call, without
/// ever reporting fewer bytes as end-of-stream (only a true 0-byte read means end-of-stream). This is ordinary,
/// contractually-valid <see cref="Stream"/> behaviour — e.g. a TLS-backed S3 response stream — but is NOT what
/// <see cref="MemoryStream"/> does (it always fills the caller's buffer in one call), so the other tests in this
/// file never exercise a short read.
/// </summary>
file sealed class ChunkedStream(byte[] data, params int[] chunkSizes) : Stream
{
    private int _position;
    private int _chunkCall;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_position >= data.Length)
            return 0;

        var chunkSize = chunkSizes.Length == 0
            ? count
            : chunkSizes[Math.Min(_chunkCall, chunkSizes.Length - 1)];
        _chunkCall++;

        var toCopy = Math.Min(Math.Min(chunkSize, count), data.Length - _position);
        Array.Copy(data, _position, buffer, offset, toCopy);
        _position += toCopy;
        await Task.Yield();
        return toCopy;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var array = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = ReadAsync(array, 0, buffer.Length, cancellationToken).GetAwaiter().GetResult();
            array.AsSpan(0, read).CopyTo(buffer.Span);
            return new ValueTask<int>(read);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(array);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
}

public class BufferedStreamLineReaderTests
{
    [Fact]
    public async Task OneLinerShouldWork()
    {
        var contents = "Hello world"u8.ToArray();
        using var ms = new MemoryStream(contents);
        var reader = new BufferedStreamLineReader(ms);
        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Length > 0);
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(contents));
        Assert.False(await reader.ReadAsync(default));
    }
    
    [Fact]
    public async Task TwoLinerShouldWork()
    {
        var hello = "Hello"u8.ToArray();
        var world = "world"u8.ToArray();
        byte[] contents = [..hello, .."\n"u8.ToArray(), ..world];
        using var ms = new MemoryStream(contents);
        var reader = new BufferedStreamLineReader(ms);
        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Length > 0);
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(hello));
        
        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Length > 0);
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(world));
        
        Assert.False(await reader.ReadAsync(default));
    }
    
    [Fact]
    public async Task MultiLinerShouldWork()
    {
        var lines = Enumerable.Range(1, 10_000_000).Select(i => i.ToString());
        var contents = Encoding.UTF8.GetBytes(string.Join("\n", lines));
        
        using var ms = new MemoryStream(contents);
        var reader = new BufferedStreamLineReader(ms);
        foreach (var line in lines)
        {
            Assert.True(await reader.ReadAsync(default));
            Assert.True(reader.CurrentLineBytes.Length > 0);
            Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(line)));
        }

        Assert.False(await reader.ReadAsync(default));
    }

    /// <summary>
    /// Wadman-IT/Primodel#616: a short first read (e.g. 43 bytes off a longer line) must not be mistaken for
    /// end-of-stream. Previously, <c>ReadMoreAsync</c> declared end-of-stream whenever a single <c>ReadAsync</c>
    /// call returned fewer bytes than the internal buffer size — but a <see cref="Stream"/> read is allowed to
    /// return a short read for reasons that have nothing to do with EOF (network chunking, TLS record boundaries,
    /// etc.); only a 0-byte read means EOF. The bug made the reader hand back the first 43 bytes of a 198-byte
    /// line as if it were a whole line.
    /// </summary>
    [Fact]
    public async Task ShortFirstReadDoesNotTruncateTheLine()
    {
        var line1 = Encoding.UTF8.GetBytes(new string('x', 198));
        var line2 = "second"u8.ToArray();
        byte[] contents = [..line1, .."\n"u8.ToArray(), ..line2];

        // First ReadAsync call returns only 43 bytes (shorter than line 1 and shorter than the reader's buffer);
        // every subsequent call returns the rest.
        var stream = new ChunkedStream(contents, 43, contents.Length);
        var reader = new BufferedStreamLineReader(stream, readBufferSize: 4096);

        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(line1));

        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(line2));

        Assert.False(await reader.ReadAsync(default));
    }

    /// <summary>Same defect, pushed to the extreme: every single <c>ReadAsync</c> call returns exactly one byte.</summary>
    [Fact]
    public async Task OneBytePerReadDoesNotTruncateTheLine()
    {
        var line1 = Encoding.UTF8.GetBytes(new string('x', 198));
        var line2 = "second"u8.ToArray();
        byte[] contents = [..line1, .."\n"u8.ToArray(), ..line2];

        var stream = new ChunkedStream(contents, 1);
        var reader = new BufferedStreamLineReader(stream, readBufferSize: 4096);

        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(line1));

        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(line2));

        Assert.False(await reader.ReadAsync(default));
    }
}