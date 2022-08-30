using System.Diagnostics;
using System.Linq.Expressions;

namespace CloudMesh.DataBlocks
{
    public class RoundRobinDataBlock : DataBlock
    {
        public RoundRobinDataBlock()
            : base(1)
        {
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

        protected override ValueTask BeforeStart()
        {
            var currentTarget = 0;
            ReceiveAnyAsync(async msg =>
            {
                var targets = Children.ToArray();

                if (targets.Length == 0)
                    return;

                // Try for three rounds to find a worker channel that is free
                // starting from the current offset and looping around three times.
                for (var loopIter = 0; loopIter < 10; loopIter++)
                {
                    for (var i = 0; i < targets.Length; i++)
                    {
                        var index = (currentTarget + i) % targets.Length;
                        var target = targets[index];
                        if (target.TrySubmit(msg, this))
                        {
                            currentTarget = (index + 1) % targets.Length;
                            return;
                        }
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
                if (++currentTarget > targets.Length - 1)
                    currentTarget = 0;
                await nextTarget.SubmitAsync(msg, this);
            });

            return TaskHelper.CompletedTask;
        }
    }
}
