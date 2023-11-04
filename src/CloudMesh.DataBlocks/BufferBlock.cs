using System.Diagnostics;

namespace CloudMesh.DataBlocks
{
    public abstract class BufferBlock<T> : DataBlock
    {
        private readonly List<object> messages;
        private readonly int maxCapacity;
        private readonly TimeSpan maxWaitTimeToFlush;
        private DateTime arrivalTimeFirstMessageOfBatch = DateTime.Now;
        private ICancelable? flushTimer = null;

        private class Flush
        {
            public static readonly Flush Instance = new();
        }

        public BufferBlock(int maxCapacity, TimeSpan maxWaitTimeToFlush)
            : base(maxCapacity)
        {
            this.maxCapacity = maxCapacity;
            this.maxWaitTimeToFlush = maxWaitTimeToFlush;
            messages = new List<object>(maxCapacity);

            ReceiveAsync<Flush>(_ =>
            {
                Debug.WriteLine($"[{Path}]: Flush signal received");
                return InternalFlushAsync();
            });

            if (typeof(T) == typeof(object))
                ReceiveAnyAsync(OnReceive);
            else
            {
                ReceiveAsync<T>(msg => OnReceive(msg!));
                ReceiveAsync<T[]>(msgs => OnReceive(msgs!));
            }
        }

        private ValueTask OnReceive(object message)
        {
            if (messages.Count == 0) // First message of new batch?
                arrivalTimeFirstMessageOfBatch = DateTime.Now;
            if (message is T[])
                messages.AddRange((object[])message);
            else
                messages.Add(message);
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

                arrivalTimeFirstMessageOfBatch = DateTime.Now;

                await Impl();
            }

            ValueTask Impl()
            {
                var timeElapsedSinceFirstMessage = DateTime.Now - arrivalTimeFirstMessageOfBatch;
                var hasMaxWaitTimePassed = timeElapsedSinceFirstMessage >= maxWaitTimeToFlush;

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
                // Start flush timer, so we guarantee a flush, even if no more messages arrive, or
                // the next message arrives after the maximum time to wait.

                if (flushTimer is null)
                {
                    var timeToNextFlush = maxWaitTimeToFlush - timeElapsedSinceFirstMessage;
                    flushTimer = DataBlockScheduler.ScheduleTellOnceCancelable(this, timeToNextFlush, Flush.Instance, this);
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

            var copy = messages
                .Take(maxCapacity)
                .Cast<T>()
                .ToArray();

            Debug.WriteLine($"[{Path}] Flushing {copy.Length} messages");

            await FlushAsync(copy);

            if (copy.Length == messages.Count)
                messages.Clear();
            else
                messages.RemoveRange(0, copy.Length);
        }

        protected abstract ValueTask FlushAsync(T[] messages);
    }
}
