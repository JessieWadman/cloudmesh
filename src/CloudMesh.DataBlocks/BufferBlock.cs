using System.Diagnostics;
using CloudMesh.Variant;

namespace CloudMesh.DataBlocks
{
    /// <summary>
    /// A fan-in block that batches incoming <typeparamref name="T"/> messages and flushes them as an array, so
    /// downstream work happens in efficient chunks rather than per message. A batch is flushed when it reaches
    /// <c>maxCapacity</c>, or when <c>maxWaitTimeToFlush</c> elapses after the batch's first message — whichever
    /// comes first — and always on shutdown.
    /// </summary>
    /// <typeparam name="T">The message type to buffer.</typeparam>
    /// <remarks>
    /// Accepts single <typeparamref name="T"/> messages and <typeparamref name="T"/><c>[]</c> arrays (which are
    /// appended to the current batch). A time-based flush is guaranteed even if no further messages arrive, via a
    /// one-shot timer armed on the first message. Handling is single-threaded and ordered, so
    /// <see cref="FlushAsync(T[])"/> never overlaps itself. See <see cref="BufferRouter{T}"/> for a ready-made
    /// buffer that forwards each batch to another block.
    /// </remarks>
    public abstract class BufferBlock<T> : DataBlock
    {
        private readonly List<T> messages;
        private readonly int maxCapacity;
        private readonly long maxWaitMs;
        private long arrivalTickFirstMessageOfBatch;
        private ICancelable? flushTimer = null;

        private class Flush
        {
            public static readonly Flush Instance = new();
        }

        /// <summary>Creates the buffer block.</summary>
        /// <param name="maxCapacity">Maximum batch size; reaching it triggers an immediate flush. Also the mailbox capacity.</param>
        /// <param name="maxWaitTimeToFlush">Maximum time to hold a partial batch after its first message before flushing.</param>
        public BufferBlock(int maxCapacity, TimeSpan maxWaitTimeToFlush)
            : base(maxCapacity)
        {
            this.maxCapacity = maxCapacity;
            this.maxWaitMs = (long)maxWaitTimeToFlush.TotalMilliseconds;
            messages = new List<T>(maxCapacity);

            ReceiveAsync<Flush>(_ =>
            {
                Debug.WriteLine($"[{Path}]: Flush signal received");
                return InternalFlushAsync();
            });

            if (typeof(T) == typeof(object))
                ReceiveAnyAsync(OnAnyReceived);
            else
            {
                ReceiveAsync<T>(OnOneReceived);
                ReceiveAsync<T[]>(OnManyReceived);
            }
        }

        private ValueTask OnOneReceived(T one)
        {
            if (messages.Count == 0) // First message of new batch?
                arrivalTickFirstMessageOfBatch = Environment.TickCount64;
            messages.Add(one);
            return FlushMaybeAsync();
        }
        
        private ValueTask OnManyReceived(T[] many)
        {
            if (messages.Count == 0) // First message of new batch?
                arrivalTickFirstMessageOfBatch = Environment.TickCount64;
            messages.AddRange(many);
            return FlushMaybeAsync();
        }
        
        private ValueTask OnAnyReceived(Value any)
        {
            if (messages.Count == 0) // First message of new batch?
                arrivalTickFirstMessageOfBatch = Environment.TickCount64;
            if (any.Type!.IsArray)
            {
                if (any.TryGetValue<T[]>(out var array))
                    OnManyReceived(array);
                else
                    return ValueTask.CompletedTask;
            }
            else if (any.TryGetValue<T>(out var single))
                OnOneReceived(single);
            else
                return ValueTask.CompletedTask;
            return FlushMaybeAsync();
        }

        protected override ValueTask AfterStop()
        {
            Debug.WriteLine($"[{Path}] Stop requested. Flushing buffer");
            return InternalFlushAsync();
        }

        private ValueTask FlushMaybeAsync()
        {
            if (messages.Count > maxCapacity)
                return new ValueTask(OverCapacity());
            else
                return Impl();

            // If we're over capacity, flush whole pages until we're below capacity, then proceeed as normal
            // with the remainder
            async Task OverCapacity()
            {
                Debug.WriteLine($"[{Path}] Buffer is over capacity");

                do
                {
                    await InternalFlushAsync();
                } while (messages.Count > maxCapacity);

                arrivalTickFirstMessageOfBatch = Environment.TickCount64;

                await Impl();
            }

            ValueTask Impl()
            {
                var elapsedMs = Environment.TickCount64 - arrivalTickFirstMessageOfBatch;
                var hasMaxWaitTimePassed = elapsedMs >= maxWaitMs;

                if (hasMaxWaitTimePassed)
                {
                    Debug.WriteLine($"[{Path}] Max wait time reached");
                    return InternalFlushAsync();
                }

                var bufferIsFull = messages.Count == maxCapacity;
                if (bufferIsFull)
                {
                    Debug.WriteLine($"[{Path}] Buffer full");
                    return InternalFlushAsync();
                }

                // We've added a message to the buffer, but not flushed, at this point.
                // Start the flush timer, so we guarantee a flush, even if no more messages arrive, or
                // the next message arrives after the maximum time to wait.

                if (flushTimer is null)
                {
                    var timeToNextFlush = TimeSpan.FromMilliseconds(maxWaitMs - elapsedMs);
                    flushTimer = DataBlockScheduler.ScheduleTellOnceCancelable(
                        this, 
                        timeToNextFlush, 
                        Flush.Instance, 
                        this);
                    Debug.WriteLine($"[{Path}] Flush timer started");
                }

                return TaskHelper.CompletedTask;
            }
        }

        private async ValueTask InternalFlushAsync()
        {
            if (flushTimer != null)
            {
                flushTimer.Cancel();
                flushTimer = null;
                Debug.WriteLine($"[{Path}] Flush timer stopped");
            }

            // In case flush timer fires at the exact same time as buffer is full.
            if (messages.Count == 0)
                return;

            // Flush at most one page (maxCapacity) at a time; the buffer may be over capacity.
            var count = Math.Min(messages.Count, maxCapacity);
            var copy = new T[count];
            messages.CopyTo(0, copy, 0, count);

            Debug.WriteLine($"[{Path}] Flushing {copy.Length} messages");

            await FlushAsync(copy);

            if (count == messages.Count)
                messages.Clear();
            else
                messages.RemoveRange(0, count);
        }

        /// <summary>
        /// Processes one flushed batch. Called when the batch fills, the wait window elapses, or the block stops.
        /// </summary>
        /// <param name="messages">The batched messages, in arrival order (never empty).</param>
        protected abstract ValueTask FlushAsync(T[] messages);
    }
}
