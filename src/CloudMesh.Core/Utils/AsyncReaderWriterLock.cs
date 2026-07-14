using System.Diagnostics.CodeAnalysis;

namespace CloudMesh.Utils;

/// <summary>
///     An awaitable reader-writer lock. Multiple readers may hold the lock at once, but a writer holds it
///     exclusively. Acquire with <see cref="ReaderLockAsync"/> or <see cref="WriterLockAsync"/> and release by
///     disposing the returned <see cref="Releaser"/> (use a <c>using</c> block).
/// </summary>
/// <remarks>
///     Prefer this over a plain <see cref="AsyncLock"/> when reads greatly outnumber writes and readers can safely
///     run concurrently. This class does not support upgradeable locks and is not re-entrant.
///     Reference: https://blogs.msdn.microsoft.com/pfxteam/2012/02/12/building-async-coordination-primitives-part-7-asyncreaderwriterlock/
/// </remarks>
/// <example>
/// <code>
/// private readonly AsyncReaderWriterLock _rw = new();
///
/// public async Task&lt;int&gt; ReadAsync()
/// {
///     using (await _rw.ReaderLockAsync())
///         return _value;            // many readers may run at once
/// }
///
/// public async Task WriteAsync(int value)
/// {
///     using (await _rw.WriterLockAsync())
///         _value = value;           // exclusive
/// }
/// </code>
/// </example>
public class AsyncReaderWriterLock
{
    private readonly Task<Releaser> _readerReleaser;
    private readonly Task<Releaser> _writerReleaser;

    private readonly Queue<TaskCompletionSource<Releaser>>
        _waitingWriters = new();

    private TaskCompletionSource<Releaser> _waitingReader = new();
    private int _readersWaiting;
    private int _status;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AsyncReaderWriterLock"/> class.
    /// </summary>
    public AsyncReaderWriterLock()
    {
        _readerReleaser = Task.FromResult(new Releaser(this, false));
        _writerReleaser = Task.FromResult(new Releaser(this, true));
    }

    /// <summary>
    ///     Asynchronously acquires a shared reader lock. Waits while a writer holds the lock or writers are queued.
    /// </summary>
    /// <returns>A <see cref="Releaser"/> whose disposal releases the reader lock.</returns>
    public Task<Releaser> ReaderLockAsync()
    {
        lock (_waitingWriters)
        {
            if (_status >= 0 && _waitingWriters.Count == 0)
            {
                ++_status;
                return _readerReleaser;
            }
            else
            {
                ++_readersWaiting;
                return _waitingReader.Task.ContinueWith(t => t.Result);
            }
        }
    }

    /// <summary>
    ///     Asynchronously acquires the exclusive writer lock. Waits until all readers and any other writer have released.
    /// </summary>
    /// <returns>A <see cref="Releaser"/> whose disposal releases the writer lock.</returns>
    public Task<Releaser> WriterLockAsync()
    {
        lock (_waitingWriters)
        {
            if (_status == 0)
            {
                _status = -1;
                return _writerReleaser;
            }
            else
            {
                var waiter = new TaskCompletionSource<Releaser>();
                _waitingWriters.Enqueue(waiter);
                return waiter.Task;
            }
        }
    }

    private void ReaderRelease()
    {
        TaskCompletionSource<Releaser>? toWake = null;

        lock (_waitingWriters)
        {
            --_status;
            if (_status == 0 && _waitingWriters.Count > 0)
            {
                _status = -1;
                toWake = _waitingWriters.Dequeue();
            }
        }

        toWake?.SetResult(new Releaser(this, true));
    }

    private void WriterRelease()
    {
        TaskCompletionSource<Releaser>? toWake = null;
        var toWakeIsWriter = false;

        lock (_waitingWriters)
        {
            if (_waitingWriters.Count > 0)
            {
                toWake = _waitingWriters.Dequeue();
                toWakeIsWriter = true;
            }
            else if (_readersWaiting > 0)
            {
                toWake = _waitingReader;
                _status = _readersWaiting;
                _readersWaiting = 0;
                _waitingReader = new TaskCompletionSource<Releaser>();
            }
            else
            {
                _status = 0;
            }
        }

        toWake?.SetResult(new Releaser(this, toWakeIsWriter));
    }

    /// <summary>
    ///     Structure for releasing lock.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct Releaser : IDisposable
    {
        private readonly AsyncReaderWriterLock _toRelease;
        private readonly bool _writer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Releaser"/> struct.
        /// </summary>
        internal Releaser(AsyncReaderWriterLock toRelease, bool writer)
        {
            _toRelease = toRelease;
            _writer = writer;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_toRelease == null)
            {
                return;
            }

            if (_writer)
            {
                _toRelease.WriterRelease();
            }
            else
            {
                _toRelease.ReaderRelease();
            }
        }
    }
}