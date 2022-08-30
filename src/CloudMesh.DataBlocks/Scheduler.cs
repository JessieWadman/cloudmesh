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
            var cancelation = new Cancelable();
            var stoppingToken = cancelation.Token;

            _ = Task.Run(async () =>
            {
                using (cancelation)
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

            return cancelation;
        }

        public static ICancelable ScheduleTellRepeatedly(ICanSubmit target, TimeSpan frequency, object message, IDataBlockRef sender)
        {
            var cancelation = new Cancelable();
            var stoppingToken = cancelation.Token;

            _ = Task.Run(async () =>
            {
                using (cancelation)
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

            return cancelation;
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
