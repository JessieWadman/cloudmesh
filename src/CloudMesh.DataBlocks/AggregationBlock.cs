using System.Diagnostics;
using CloudMesh.Variant;

namespace CloudMesh.DataBlocks
{
    /// <summary>
    /// A fan-in block that accumulates incoming <typeparamref name="T"/> messages into your own aggregate state
    /// and flushes it on a fixed time window. Unlike <see cref="BufferBlock{T}"/> (which batches the messages
    /// themselves), an aggregation block folds each message into custom state via <see cref="ReceiveOne(T)"/> —
    /// e.g. summing counters, merging metrics, or coalescing updates — then emits the aggregate in
    /// <see cref="FlushAsync()"/>.
    /// </summary>
    /// <typeparam name="T">The incoming message type to aggregate.</typeparam>
    /// <remarks>
    /// The flush timer starts on the first message of a batch and fires once <c>flushFrequency</c> later,
    /// triggering <see cref="FlushAsync()"/>; the block also flushes on shutdown so nothing is lost. Because it
    /// derives from <see cref="DataBlock"/>, all message handling (including your aggregation and flush) runs
    /// single-threaded and in order, so the aggregate needs no locking.
    /// </remarks>
    public abstract class AggregationDataBlock<T> : DataBlock
    {
        private bool firstMessageInBatch = true;
        private readonly TimeSpan flushFrequency;
        private ICancelable? flushTimer = null;

        private class Flush
        {
            public static readonly Flush Instance = new();
        }

        /// <summary>Creates the aggregation block.</summary>
        /// <param name="flushFrequency">Time window after the first message of a batch before the aggregate is flushed.</param>
        /// <param name="bufferSize">Mailbox capacity (backpressure bound).</param>
        protected AggregationDataBlock(TimeSpan flushFrequency, int bufferSize = 1)
            : base(bufferSize)
        {
            this.flushFrequency = flushFrequency;

            ReceiveAsync<Flush>(_ =>
            {
                Debug.WriteLine($"[{Path}]: Flush signal received");
                return InternalFlushAsync();
            });

            if (typeof(T) == typeof(object))
                ReceiveAnyAsync(InternalReceiveAny);
            else
            {
                ReceiveAsync<T>(msg =>
                {
                    if (msg is not null)
                    {
                        return InternalReceiveOne(msg);
                    }

                    return ValueTask.CompletedTask;
                });
            }
        }

        /// <summary>
        /// Folds a single incoming message into your aggregate state. Return <see langword="true"/> if the
        /// message contributed to the batch (and should therefore arm the flush timer), or <see langword="false"/>
        /// to ignore it.
        /// </summary>
        /// <param name="message">The incoming message.</param>
        /// <returns><see langword="true"/> if the message was aggregated into the current batch.</returns>
        protected abstract bool ReceiveOne(T message);

        private ValueTask InternalReceiveOne(T message)
        {
            if (!ReceiveOne(message))
                return TaskHelper.CompletedTask;

            if (firstMessageInBatch) // First message of new batch?
            {
                firstMessageInBatch = false;
                if (flushTimer is null)
                {
                    flushTimer = DataBlockScheduler.ScheduleTellOnceCancelable(
                        this, 
                        flushFrequency, 
                        Flush.Instance, 
                        this);
                    Debug.WriteLine($"[{Path}] Flush timer started");
                }
            }

            return TaskHelper.CompletedTask;
        }
        
        private ValueTask InternalReceiveAny(Value message)
        {
            if (!message.TryGetValue<T>(out var t))
                return TaskHelper.CompletedTask;
            if (!ReceiveOne(t))
                return TaskHelper.CompletedTask;

            if (firstMessageInBatch) // First message of new batch?
            {
                firstMessageInBatch = false;
                if (flushTimer is null)
                {
                    flushTimer = DataBlockScheduler.ScheduleTellOnceCancelable(this, flushFrequency, Flush.Instance, this);
                    Debug.WriteLine($"[{Path}] Flush timer started");
                }
            }

            return TaskHelper.CompletedTask;
        }

        protected override ValueTask AfterStop()
        {
            Debug.WriteLine($"[{Path}] Stop requested. Flushing buffer");
            return InternalFlushAsync();
        }

        private ValueTask InternalFlushAsync()
        {
            if (flushTimer != null)
            {
                flushTimer.Cancel();
                flushTimer = null;
                Debug.WriteLine($"[{Path}] Flush timer stopped");
                firstMessageInBatch = true;
            }

            return FlushAsync();
        }

        /// <summary>
        /// Emits and resets the accumulated aggregate. Called when the flush window elapses and on shutdown.
        /// </summary>
        protected abstract ValueTask FlushAsync();
    }
}
