using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace CloudMesh.Utils
{
    // Forked from https://github.com/neosmart/AsyncLock/blob/master/AsyncLock/AsyncLock.cs
    public class AsyncLock
    {   
        private readonly SemaphoreSlim reentrancy = new(1, 1);
        private int reentrances = 0;
        // We are using this SemaphoreSlim like a posix condition variable.
        // We only want to wake waiters, one or more of whom will try to obtain
        // a different lock to do their thing. So long as we can guarantee no
        // wakes are missed, the number of awakees is not important.
        // Ideally, this would be "friend" for access only from InnerLock, but
        // whatever.
        private readonly SemaphoreSlim retry = new(0, 1);
        private const long UnlockedId = 0x00; // "owning" task id when unlocked
        private long owningId = UnlockedId;
        private int owningThreadId = (int)UnlockedId;
        private static long _asyncStackCounter = 0;
        
        // An AsyncLocal<T> is not really the task-based equivalent to a ThreadLocal<T>, in that
        // it does not track the async flow (as the documentation describes) but rather it is
        // associated with a stack snapshot. Mutation of the AsyncLocal in an await call does
        // not change the value observed by the parent when the call returns, so if you want to
        // use it as a persistent async flow identifier, the value needs to be set at the outer-
        // most level and never touched internally.
        private static readonly AsyncLocal<long> _asyncId = new();
        private static long AsyncId => _asyncId.Value;

        private static int ThreadId => Environment.CurrentManagedThreadId;

        public AsyncLock()
        {
        }

#if !DEBUG
        readonly
#endif
        private struct InnerLock : IDisposable
        {
            private readonly AsyncLock parent;
            private readonly long oldId;
            private readonly int oldThreadId;
#if DEBUG
            private bool disposed;
#endif

            internal InnerLock(AsyncLock parent, long oldId, int oldThreadId)
            {
                this.parent = parent;
                this.oldId = oldId;
                this.oldThreadId = oldThreadId;
#if DEBUG
                disposed = false;
#endif
            }

            internal async Task<IDisposable> ObtainLockAsync(CancellationToken ct = default)
            {
                while (!await TryEnterAsync(ct))
                {
                    // We need to wait for someone to leave the lock before trying again.
                    await parent.retry.WaitAsync(ct);
                }
                // Reset the owning thread id after all await calls have finished, otherwise we
                // could be resumed on a different thread and set an incorrect value.
                parent.owningThreadId = ThreadId;
                // In case of !synchronous and success, TryEnter() does not release the reentrancy lock
                parent.reentrancy.Release();
                return this;
            }

            internal async Task<IDisposable?> TryObtainLockAsync(TimeSpan timeout)
            {
                // In case of zero-timeout, don't even wait for protective lock contention
                if (timeout == TimeSpan.Zero)
                {
                    if (await TryEnterAsync(timeout))
                    {
                        return this;
                    }
                    return null;
                }

                var now = DateTimeOffset.UtcNow;
                var last = now;
                var remainder = timeout;

                // We need to wait for someone to leave the lock before trying again.
                while (remainder > TimeSpan.Zero)
                {
                    if (await TryEnterAsync(remainder))
                    {
                        // Reset the owning thread id after all await calls have finished, otherwise we
                        // could be resumed on a different thread and set an incorrect value.
                        parent.owningThreadId = ThreadId;
                        // In case of !synchronous and success, TryEnter() does not release the reentrancy lock
                        parent.reentrancy.Release();
                        return this;
                    }

                    now = DateTimeOffset.UtcNow;
                    remainder -= now - last;
                    last = now;
                    if (remainder < TimeSpan.Zero || !await parent.retry.WaitAsync(remainder))
                    {
                        return null;
                    }

                    now = DateTimeOffset.UtcNow;
                    remainder -= now - last;
                    last = now;
                }

                return null;
            }

            internal async Task<IDisposable?> TryObtainLockAsync(CancellationToken cancel)
            {
                try
                {
                    while (!await TryEnterAsync(cancel))
                    {
                        // We need to wait for someone to leave the lock before trying again.
                        await parent.retry.WaitAsync(cancel);
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                // Reset the owning thread id after all await calls have finished, otherwise we
                // could be resumed on a different thread and set an incorrect value.
                parent.owningThreadId = ThreadId;
                // In case of !synchronous and success, TryEnter() does not release the reentrancy lock
                parent.reentrancy.Release();
                return this;
            }

            internal IDisposable ObtainLock(CancellationToken cancellationToken)
            {
                while (!TryEnter())
                {
                    // We need to wait for someone to leave the lock before trying again.
                    parent.retry.Wait(cancellationToken);
                }
                return this;
            }

            internal IDisposable? TryObtainLock(TimeSpan timeout)
            {
                // In case of zero-timeout, don't even wait for protective lock contention
                if (timeout == TimeSpan.Zero)
                {
                    if (TryEnter(timeout))
                    {
                        return this;
                    }
                    return null;
                }

                var now = DateTimeOffset.UtcNow;
                var last = now;
                var remainder = timeout;

                // We need to wait for someone to leave the lock before trying again.
                while (remainder > TimeSpan.Zero)
                {
                    if (TryEnter(remainder))
                    {
                        return this;
                    }

                    now = DateTimeOffset.UtcNow;
                    remainder -= now - last;
                    last = now;
                    if (!parent.retry.Wait(remainder))
                    {
                        return null;
                    }

                    now = DateTimeOffset.UtcNow;
                    remainder -= now - last;
                    last = now;
                }

                return null;
            }

            private async Task<bool> TryEnterAsync(CancellationToken cancel = default)
            {
                await parent.reentrancy.WaitAsync(cancel);
                return InnerTryEnter();
            }

            private async Task<bool> TryEnterAsync(TimeSpan timeout)
            {
                if (!await parent.reentrancy.WaitAsync(timeout))
                {
                    return false;
                }

                return InnerTryEnter();
            }

            private bool TryEnter()
            {
                parent.reentrancy.Wait();
                return InnerTryEnter(true /* synchronous */);
            }

            private bool TryEnter(TimeSpan timeout)
            {
                if (!parent.reentrancy.Wait(timeout))
                {
                    return false;
                }
                return InnerTryEnter(true /* synchronous */);
            }

            private bool InnerTryEnter(bool synchronous = false)
            {
                var result = false;
                try
                {
                    if (synchronous)
                    {
                        if (parent.owningThreadId == UnlockedId)
                        {
                            parent.owningThreadId = ThreadId;
                        }
                        else if (parent.owningThreadId != ThreadId)
                        {
                            return false;
                        }
                        parent.owningId = AsyncLock.AsyncId;
                    }
                    else
                    {
                        if (parent.owningId == UnlockedId)
                        {
                            // Obtain a new async stack ID
                            //_asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);
                            parent.owningId = AsyncLock.AsyncId;
                        }
                        else if (parent.owningId != oldId)
                        {
                            // Another thread currently owns the lock
                            return false;
                        }
                        else
                        {
                            // Nested re-entrance
                            parent.owningId = AsyncId;
                        }
                    }

                    // We can go in
                    Interlocked.Increment(ref parent.reentrances);
                    result = true;
                    return result;
                }
                finally
                {
                    // We can't release this in case the lock was obtained because we still need to
                    // set the owning thread id, but we may have been called asynchronously in which
                    // case we could be currently running on a different thread than the one the
                    // locking will ultimately conclude on.
                    if (!result || synchronous)
                    {
                        parent.reentrancy.Release();
                    }
                }
            }

            public void Dispose()
            {
#if DEBUG
                Debug.Assert(!disposed);
                disposed = true;
#endif
                var @this = this;
                var oldId = this.oldId;
                var oldThreadId = this.oldThreadId;
                Task.Run(async () =>
                {
                    await @this.parent.reentrancy.WaitAsync();
                    try
                    {
                        Interlocked.Decrement(ref @this.parent.reentrances);
                        @this.parent.owningId = oldId;
                        @this.parent.owningThreadId = oldThreadId;
                        if (@this.parent.reentrances == 0)
                        {
                            // The owning thread is always the same so long as we
                            // are in a nested stack call. We reset the owning id
                            // only when the lock is fully unlocked.
                            @this.parent.owningId = UnlockedId;
                            @this.parent.owningThreadId = (int)UnlockedId;
                            if (@this.parent.retry.CurrentCount == 0)
                            {
                                @this.parent.retry.Release();
                            }
                        }
                    }
                    finally
                    {
                        @this.parent.reentrancy.Release();
                    }
                });
            }
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<IDisposable> LockAsync(CancellationToken ct = default)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock._asyncStackCounter);
            return @lock.ObtainLockAsync(ct);
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<bool> TryLockAsync(Action callback, TimeSpan timeout)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock._asyncStackCounter);

            return @lock.TryObtainLockAsync(timeout)
                .ContinueWith(state =>
                {
                    if (state.Exception is AggregateException ex)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                    }
                    var disposableLock = state.Result;
                    if (disposableLock is null)
                    {
                        return false;
                    }

                    try
                    {
                        callback();
                    }
                    finally
                    {
                        disposableLock.Dispose();
                    }
                    return true;
                });
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<bool> TryLockAsync(Func<Task> callback, TimeSpan timeout)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock._asyncStackCounter);

            return @lock.TryObtainLockAsync(timeout)
                .ContinueWith(state =>
                {
                    if (state.Exception is AggregateException ex)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                    }
                    var disposableLock = state.Result;
                    if (disposableLock is null)
                    {
                        return Task.FromResult(false);
                    }

                    return callback()
                        .ContinueWith(result =>
                        {
                            disposableLock.Dispose();

                            if (result.Exception is AggregateException ex)
                            {
                                ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                            }

                            return true;
                        });
                }).Unwrap();
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<bool> TryLockAsync(Action callback, CancellationToken cancel)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock._asyncStackCounter);

            return @lock.TryObtainLockAsync(cancel)
                .ContinueWith(state =>
                {
                    if (state.Exception is AggregateException ex)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                    }
                    var disposableLock = state.Result;
                    if (disposableLock is null)
                    {
                        return false;
                    }

                    try
                    {
                        callback();
                    }
                    finally
                    {
                        disposableLock.Dispose();
                    }
                    return true;
                });
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<bool> TryLockAsync(Func<Task> callback, CancellationToken cancel)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock._asyncStackCounter);

            return @lock.TryObtainLockAsync(cancel)
                .ContinueWith(state =>
                {
                    if (state.Exception is AggregateException ex)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                    }
                    var disposableLock = state.Result;
                    if (disposableLock is null)
                    {
                        return Task.FromResult(false);
                    }

                    return callback()
                        .ContinueWith(result =>
                        {
                            disposableLock.Dispose();

                            if (result.Exception is AggregateException ex)
                            {
                                ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                            }

                            return true;
                        });
                }).Unwrap();
        }

        public IDisposable Lock(CancellationToken cancellationToken = default)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            // Increment the async stack counter to prevent a child task from getting
            // the lock at the same time as a child thread.
            _asyncId.Value = Interlocked.Increment(ref AsyncLock._asyncStackCounter);
            return @lock.ObtainLock(cancellationToken);
        }

        public bool TryLock(Action callback, TimeSpan timeout)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            // Increment the async stack counter to prevent a child task from getting
            // the lock at the same time as a child thread.
            _asyncId.Value = Interlocked.Increment(ref AsyncLock._asyncStackCounter);
            var lockDisposable = @lock.TryObtainLock(timeout);
            if (lockDisposable is null)
            {
                return false;
            }

            // Execute the callback then release the lock
            try
            {
                callback();
            }
            finally
            {
                lockDisposable.Dispose();
            }
            return true;
        }
    }
}
