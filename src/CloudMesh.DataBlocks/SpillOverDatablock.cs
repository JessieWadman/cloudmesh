using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace CloudMesh.DataBlocks
{
    /// <summary>
    /// A fan-out block that keeps sending to the <b>same</b> worker until it fills up (backpressure), only then
    /// spilling over to the next. This is the opposite bias to <see cref="RoundRobinDataBlock"/>'s even spread:
    /// it favours few, large batches on a single worker rather than a uniform distribution.
    /// </summary>
    /// <remarks>
    /// Useful for downstream writers where big batches are much cheaper than many small ones — e.g. Amazon
    /// Timestream or CloudWatch metric writes — so you want as few batch-writes as possible, each as large as
    /// possible. Add workers with <see cref="AddTarget{T}"/> / <see cref="AddTargets{T}"/>.
    /// </remarks>
    public class SpillOverDataBlock : DataBlock
    {
        private readonly bool advanceOnSuccess;

        /// <summary>Creates the spill-over router.</summary>
        /// <param name="advanceOnSuccess">When <see langword="false"/> (default), keep hitting the same worker
        /// until it applies backpressure; when <see langword="true"/>, advance after every successful send.</param>
        public SpillOverDataBlock(bool advanceOnSuccess = false)
            : base(1)
        {
            this.advanceOnSuccess = advanceOnSuccess;
        }

        /// <summary>Adds a worker to the pool from a <c>() =&gt; new T(...)</c> expression.</summary>
        /// <typeparam name="T">The worker block type.</typeparam>
        /// <param name="newExpression">A <c>() =&gt; new T(...)</c> expression describing the worker.</param>
        public void AddTarget<T>(Expression<Func<T>> newExpression) where T : IDataBlock
        {
            ChildOf(newExpression, $"{Children.Count()}");
        }

        /// <summary>Adds <paramref name="count"/> identical workers to the pool.</summary>
        /// <typeparam name="T">The worker block type.</typeparam>
        /// <param name="newExpression">A <c>() =&gt; new T(...)</c> expression describing each worker.</param>
        /// <param name="count">The number of workers to create.</param>
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
