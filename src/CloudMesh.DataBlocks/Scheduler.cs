namespace CloudMesh.DataBlocks
{
    public interface ICancelable
    {
        void Cancel();
    }

    public static class DataBlockScheduler
    {
        public static void ScheduleTellOnce(ICanSubmit target, TimeSpan delay, object message, IDataBlockRef sender)
        {
            _ = Task.Run(async () =>
            {
                if (delay != TimeSpan.Zero)
                    await Task.Delay(delay);
                await target.SubmitAsync(message, sender);
            }).ConfigureAwait(false);
        }

        public static ICancelable ScheduleTellOnceCancelable(ICanSubmit target, TimeSpan delay, object message, IDataBlockRef sender)
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

        public static ICancelable ScheduleTellRepeatedly(ICanSubmit target, TimeSpan frequency, object message, IDataBlockRef sender)
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

        public class Cancelable : ICancelable, IDisposable
        {
            private CancellationTokenSource? CancellationTokenSource = new();

            public CancellationToken Token => CancellationTokenSource is null ? default : CancellationTokenSource.Token;

            public void Cancel()
            {
                lock (this)
                {
                    CancellationTokenSource?.Cancel();
                }
            }

            public void Dispose()
            {
                lock (this)
                {
                    CancellationTokenSource?.Dispose();
                    CancellationTokenSource = null;
                }
                GC.SuppressFinalize(this);
            }
        }
    }
}
