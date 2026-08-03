using System.Buffers;
using Microsoft.Win32.SafeHandles;

namespace CloudMesh.IO;

/// <summary>
/// A seekable read/write stream backed by a bounded, paged cache that spills to a temp file only when it has to.
/// The logical stream is divided into fixed-size pages; at most a handful are kept resident in pooled memory (an
/// LRU working set), so RAM stays bounded no matter how large the stream grows. A page is faulted in on access and,
/// if dirty, written back to a lazily-created temp file when it is evicted. Frequently-touched regions therefore stay
/// pure memory operations, while cold regions live on disk.
/// </summary>
/// <remarks>
/// <para>
/// This is built for workloads that must land a byte stream into a seekable store and then read it back with random
/// access — for example columnar file formats (Parquet keeps its schema/footer at the <em>end</em> of the file, so
/// the reader seeks to the tail and then jumps around). A naive "spill everything to a temp file" approach pays disk
/// I/O even for a 7 KB payload and re-reads the footer from disk on every access; a plain <see cref="MemoryStream"/>
/// buffers the entire file in RAM and OOMs on a 12 GB input. <see cref="SpillStream"/> sits between the two: small
/// payloads never touch disk (they fit inside the resident page set), large payloads stay bounded to
/// <c>pageSize × maxResidentPages</c> of RAM, and hot pages (the footer) are served from memory after the first read.
/// </para>
/// <para>
/// The cold tier uses handle-based positioned I/O (<see cref="RandomAccess"/>) — cross-platform (pwritev/preadv on
/// Linux, overlapped I/O on Windows), no per-operation position state — and is opened <c>DeleteOnClose</c> so the temp
/// file is reclaimed on dispose. The temp file is only created the first time a dirty page is evicted; a stream that
/// never overflows its resident set never creates one (<see cref="SpilledToDisk"/> stays <c>false</c>).
/// </para>
/// <para>
/// The stream is not thread-safe; use one instance per logical operation. Pages not written to (gaps created by
/// seeking past the end and writing) read back as zeros.
/// </para>
/// </remarks>
public sealed class SpillStream : Stream
{
    /// <summary>Default page size (128 KiB) — large enough to amortize disk I/O, small enough to stay in the array pool.</summary>
    public const int DefaultPageSize = 128 * 1024;

    /// <summary>Default resident page budget (16 pages ≈ 2 MiB at the default page size).</summary>
    public const int DefaultMaxResidentPages = 16;

    private sealed class Page(long index, byte[] buffer)
    {
        public readonly long Index = index;
        public readonly byte[] Buffer = buffer;
        public bool Dirty;
    }

    private readonly int _pageSize;
    private readonly int _maxResidentPages;
    private readonly Dictionary<long, LinkedListNode<Page>> _resident = new();
    private readonly LinkedList<Page> _lru = new();     // first = most-recently-used, last = eviction victim
    private readonly HashSet<long> _persisted = new();  // page indices that have been written to the temp file

    private SafeFileHandle? _cold;
    private long _length;
    private long _position;
    private bool _disposed;

    /// <summary>Creates a spill stream with the given page size and resident-page budget.</summary>
    /// <param name="pageSize">Size of each page in bytes. Kept ≤ 1 MiB so pages come from <see cref="ArrayPool{T}.Shared"/>.</param>
    /// <param name="maxResidentPages">Maximum pages held in memory at once (must be ≥ 1). RAM is bounded to <c>pageSize × this</c>.</param>
    public SpillStream(int pageSize = DefaultPageSize, int maxResidentPages = DefaultMaxResidentPages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResidentPages, 1);
        _pageSize = pageSize;
        _maxResidentPages = maxResidentPages;
    }

    /// <summary>True once at least one page has been written to the temp file (the stream outgrew its resident budget).</summary>
    public bool SpilledToDisk => _cold is not null;

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => !_disposed;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0) throw new IOException("Cannot seek before the start of the stream.");
        return _position = target;
    }

    public override void SetLength(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        _length = value;
        if (_position > _length) _position = _length;
    }

    // Valid bytes of a page given the current length (the last page is short; a fully-past-end page is empty).
    private int ValidLength(long index)
    {
        var remaining = _length - index * (long)_pageSize;
        if (remaining <= 0) return 0;
        return remaining >= _pageSize ? _pageSize : (int)remaining;
    }

    // ── Write ──────────────────────────────────────────────────────────────────────────────────
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();
        while (!buffer.IsEmpty)
        {
            var offset = (int)(_position % _pageSize);
            var n = Math.Min(buffer.Length, _pageSize - offset);
            var page = GetPage(_position / _pageSize);
            buffer[..n].CopyTo(page.Buffer.AsSpan(offset, n));
            page.Dirty = true;
            Advance(n);
            buffer = buffer[n..];
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        while (!buffer.IsEmpty)
        {
            var offset = (int)(_position % _pageSize);
            var n = Math.Min(buffer.Length, _pageSize - offset);
            var page = await GetPageAsync(_position / _pageSize, cancellationToken).ConfigureAwait(false);
            buffer.Span[..n].CopyTo(page.Buffer.AsSpan(offset, n));
            page.Dirty = true;
            Advance(n);
            buffer = buffer[n..];
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Advance(int written)
    {
        _position += written;
        if (_position > _length) _length = _position;
    }

    // ── Read ───────────────────────────────────────────────────────────────────────────────────
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        var toRead = (int)Math.Min(buffer.Length, _length - _position);
        var total = 0;
        while (toRead > 0)
        {
            var offset = (int)(_position % _pageSize);
            var n = Math.Min(toRead, _pageSize - offset);
            var page = GetPage(_position / _pageSize);
            page.Buffer.AsSpan(offset, n).CopyTo(buffer.Slice(total, n));
            _position += n;
            total += n;
            toRead -= n;
        }
        return total;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var toRead = (int)Math.Min(buffer.Length, _length - _position);
        var total = 0;
        while (toRead > 0)
        {
            var offset = (int)(_position % _pageSize);
            var n = Math.Min(toRead, _pageSize - offset);
            var page = await GetPageAsync(_position / _pageSize, cancellationToken).ConfigureAwait(false);
            page.Buffer.AsSpan(offset, n).CopyTo(buffer.Span.Slice(total, n));
            _position += n;
            total += n;
            toRead -= n;
        }
        return total;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    // ── Paging ─────────────────────────────────────────────────────────────────────────────────
    private Page GetPage(long index)
    {
        if (_resident.TryGetValue(index, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value;
        }

        var buffer = RentZeroed();
        if (_persisted.Contains(index))
        {
            var valid = ValidLength(index);
            if (valid > 0) RandomAccess.Read(_cold!, buffer.AsSpan(0, valid), index * (long)_pageSize);
        }

        var page = new Page(index, buffer);
        _resident[index] = _lru.AddFirst(page);
        if (_resident.Count > _maxResidentPages) Evict();
        return page;
    }

    private async ValueTask<Page> GetPageAsync(long index, CancellationToken cancellationToken)
    {
        if (_resident.TryGetValue(index, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value;
        }

        var buffer = RentZeroed();
        if (_persisted.Contains(index))
        {
            var valid = ValidLength(index);
            if (valid > 0) await RandomAccess.ReadAsync(_cold!, buffer.AsMemory(0, valid), index * (long)_pageSize, cancellationToken).ConfigureAwait(false);
        }

        var page = new Page(index, buffer);
        _resident[index] = _lru.AddFirst(page);
        if (_resident.Count > _maxResidentPages) await EvictAsync(cancellationToken).ConfigureAwait(false);
        return page;
    }

    private void Evict()
    {
        var page = _lru.Last!.Value;
        if (page.Dirty)
        {
            EnsureCold();
            var valid = ValidLength(page.Index);
            if (valid > 0) RandomAccess.Write(_cold!, page.Buffer.AsSpan(0, valid), page.Index * (long)_pageSize);
            _persisted.Add(page.Index);
        }
        Retire(page);
    }

    private async ValueTask EvictAsync(CancellationToken cancellationToken)
    {
        var page = _lru.Last!.Value;
        if (page.Dirty)
        {
            EnsureCold();
            var valid = ValidLength(page.Index);
            if (valid > 0) await RandomAccess.WriteAsync(_cold!, page.Buffer.AsMemory(0, valid), page.Index * (long)_pageSize, cancellationToken).ConfigureAwait(false);
            _persisted.Add(page.Index);
        }
        Retire(page);
    }

    private void Retire(Page page)
    {
        _lru.RemoveLast();
        _resident.Remove(page.Index);
        ArrayPool<byte>.Shared.Return(page.Buffer);
    }

    private byte[] RentZeroed()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_pageSize);
        Array.Clear(buffer, 0, _pageSize); // pooled memory is dirty; unwritten regions must read back as zeros
        return buffer;
    }

    private void EnsureCold() => _cold ??= File.OpenHandle(
        Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
        FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose);

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ── Lifetime ───────────────────────────────────────────────────────────────────────────────
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void ReleasePages()
    {
        foreach (var node in _resident.Values)
            ArrayPool<byte>.Shared.Return(node.Value.Buffer);
        _resident.Clear();
        _lru.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            ReleasePages();
            _cold?.Dispose(); // DeleteOnClose reclaims the temp file
            _cold = null;
        }
        _disposed = true;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            ReleasePages();
            _cold?.Dispose();
            _cold = null;
            _disposed = true;
        }
        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
