using System.Diagnostics;
using CloudMesh.Variant;

namespace CloudMesh.DataBlocks
{
    public abstract class AggregationDataBlock<T> : DataBlock
    {
        private bool firstMessageInBatch = true;
        private readonly TimeSpan flushFrequency;
        private ICancelable? flushTimer = null;

        private class Flush
        {
            public static readonly Flush Instance = new();
        }

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

        protected abstract ValueTask FlushAsync();
    }
}
