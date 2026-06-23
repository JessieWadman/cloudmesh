using System.Diagnostics;
using CloudMesh.Variant;

namespace CloudMesh.DataBlocks
{
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

        protected abstract ValueTask FlushAsync(T[] messages);
    }
}
