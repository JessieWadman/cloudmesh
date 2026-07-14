using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace CloudMesh.DataBlocks
{
    /// <summary>
    /// A fan-out block that distributes each incoming message across a pool of child worker blocks, spreading load
    /// <b>evenly</b> by advancing to the next worker on every message. If the chosen worker is applying
    /// backpressure (full mailbox), it tries the others, and ultimately awaits the next worker so no message is
    /// dropped. Use it when you want work spread as uniformly as possible across workers.
    /// </summary>
    /// <remarks>
    /// Add workers with <see cref="AddTarget{T}"/> / <see cref="AddTargets{T}"/> (they become supervised children).
    /// Contrast with <see cref="SpillOverDataBlock"/>, which prefers to keep hitting the same worker until it fills
    /// up (favouring fewer, larger batches).
    /// </remarks>
    public class RoundRobinDataBlock : DataBlock
    {
        private readonly bool advanceOnSuccess;

        /// <summary>Creates the round-robin router.</summary>
        /// <param name="advanceOnSuccess">When <see langword="true"/> (default), advance to the next worker after
        /// every successful send, giving an even spread; when <see langword="false"/>, only advance on failure.</param>
        public RoundRobinDataBlock(bool advanceOnSuccess = true)
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
            for (var i = 0; i < count; i++)
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

            return TaskHelper.CompletedTask;
        }
    }
}
