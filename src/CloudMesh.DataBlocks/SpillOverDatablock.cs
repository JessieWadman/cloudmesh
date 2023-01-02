using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace CloudMesh.DataBlocks
{
    /// <summary>
    /// This differs from round-robin in that round-robin will always advance to the next one 
    /// whenever a message is written.
    /// This will only advance to the next when a write fails.
    /// I.e. it will continuously write to the same one, until backpressure forms, and then move to the next
    /// This is useful for writers where you want few, big writes, rather than even spread. 
    /// This lowers cost for things like Timestream and CloudWatch metrics, where you want as few
    /// batch-writes as possible, with as big a batch as possible.
    /// </summary>
    public class SpillOverDataBlock : DataBlock
    {
        private readonly bool advanceOnSuccess;

        public SpillOverDataBlock(bool advanceOnSuccess = false)
            : base(1)
        {
            this.advanceOnSuccess = advanceOnSuccess;
        }

        public void AddTarget<T>(Expression<Func<T>> newExpression) where T : IDataBlock
        {
            ChildOf(newExpression, $"{Children.Count()}");
        }

        public void AddTargets<T>(Expression<Func<T>> newExpression, int count) where T : IDataBlock
        {
            for (int i = 0; i < count; i++)
                AddTarget(newExpression);
        }

        private int currentTarget = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Advance(int max, int advanceBy = 1)
        {
            currentTarget = (currentTarget + advanceBy) % max;
        }

        protected override ValueTask BeforeStart()
        {
            ReceiveAnyAsync(async msg =>
            {
                var targets = Children.ToArray();
                var targetCount = targets.Length;

                if (targetCount == 0)
                    return;

                // Ensure we're not out of bounds
                Advance(targetCount, 0);

                // Try for three rounds to find a worker channel that is free
                // starting from the current offset and looping around three times.
                for (var loopIter = 0; loopIter < 10; loopIter++)
                {
                    for (var i = 0; i < targets.Length; i++)
                    {
                        var target = targets[currentTarget];
                        if (target.TrySubmit(msg, this))
                        {
                            if (advanceOnSuccess)
                                Advance(targetCount);
                            return;
                        }
                        Advance(targetCount);
                    }
                    await Task.Yield();
                    await Task.Delay(1);
                }

                // Backpressure detected, at this point.
                Debug.WriteLine($"[{Path}] Backpressure detected, waiting to send message to next channel.");
                try
                {
                    BackpressureMonitor.OnBackpressureDetected?.Invoke(Path);
                }
                catch { }

                // If that fails (caused by backpressure), push it to the next one
                // according to round-robin, and advance by one.
                var nextTarget = targets[currentTarget];
                await nextTarget.SubmitAsync(msg, this);
                if (advanceOnSuccess)
                    Advance(targetCount);
            });

            return ValueTask.CompletedTask;
        }
    }
}
