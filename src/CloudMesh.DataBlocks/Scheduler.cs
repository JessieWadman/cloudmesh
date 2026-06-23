namespace CloudMesh.DataBlocks
{
    public interface ICancelable
    {
        void Cancel();
    }

    public static class DataBlockScheduler
    {
        public static void ScheduleTellOnce<T>(ICanSubmit target, TimeSpan delay, T message, IDataBlockRef? sender)
        {
            _ = Task.Run(async () =>
            {
                if (delay != TimeSpan.Zero)
                    await Task.Delay(delay);
                await target.SubmitAsync(message, sender);
            }).ConfigureAwait(false);
        }
        
        public static void ScheduleTellOnce<T>(ICanSubmit target, int delayInMilliseconds, T message, IDataBlockRef? sender)
            => ScheduleTellOnce(target, TimeSpan.FromMilliseconds(delayInMilliseconds), message, sender);

        public static ICancelable ScheduleTellOnceCancelable<T>(ICanSubmit target, TimeSpan delay, T message, IDataBlockRef? sender)
        {
            var cancellation = new Cancelable();
            var stoppingToken = cancellation.Token;

            _ = Task.Run(async () =>
            {
                using (cancellation)
                {
                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                        if (!stoppingToken.IsCancellationRequested)
                            await target.SubmitAsync(message, sender);
                    }
                    catch (TaskCanceledException)
                    {
                    }
                }
            }).ConfigureAwait(false);

            return cancellation;
        }

        public static ICancelable ScheduleTellOnceCancelable<T>(ICanSubmit target, int delayInMilliseconds, T message,
            IDataBlockRef? sender)
            => ScheduleTellOnceCancelable(target, TimeSpan.FromMilliseconds(delayInMilliseconds), message, sender);

        public static ICancelable ScheduleTellRepeatedly<T>(ICanSubmit target, TimeSpan frequency, T message, IDataBlockRef? sender)
        {
            var cancellation = new Cancelable();
            var stoppingToken = cancellation.Token;

            _ = Task.Run(async () =>
            {
                using (cancellation)
                {
                    try
                    {
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            await Task.Delay(frequency, stoppingToken);
                            if (!stoppingToken.IsCancellationRequested)
                                await target.SubmitAsync(message, sender);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                    }
                }
            }).ConfigureAwait(false);

            return cancellation;
        }
        
        public static ICancelable ScheduleTellRepeatedly<T>(ICanSubmit target, int frequencyInMilliseconds, T message, IDataBlockRef? sender)
            => ScheduleTellRepeatedly(target, TimeSpan.FromMilliseconds(frequencyInMilliseconds), message, sender);

        public class Cancelable : ICancelable, IDisposable
        {
            private CancellationTokenSource? cancellationTokenSource = new();

            public CancellationToken Token
            {
                get
                {
                    lock(this)
                    {
                        return cancellationTokenSource?.Token ?? CancellationToken.None;
                    }
                }
            }

            public void Cancel()
            {
                lock (this)
                {
                    cancellationTokenSource?.Cancel();
                }
            }

            public void Dispose()
            {
                lock (this)
                {
                    cancellationTokenSource?.Dispose();
                    cancellationTokenSource = null;
                }
                GC.SuppressFinalize(this);
            }
        }
    }
}
