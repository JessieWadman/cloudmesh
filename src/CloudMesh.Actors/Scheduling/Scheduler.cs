using CloudMesh.Actors.Hosting;

namespace CloudMesh.Actors.Scheduling
{
    public interface ICancelable
    {
        void Cancel();
    }

    public static class Scheduler
    {
        public static void ScheduleOnce<T>(IActor target, TimeSpan delay, string methodName, params object[] args)
        {
            var hostedActor = target as IHostedActor ?? throw new InvalidOperationException("Actor must inherit from class Actor for scheduler to work.");

            _ = Task.Run(async () =>
            {
                if (delay != TimeSpan.Zero)
                    await Task.Delay(delay);
                await hostedActor.InvokeAsync(methodName, args, ActorAddress.Local, default);
            }).ConfigureAwait(false);
        }

        public static ICancelable ScheduleOnceCancelable(IActor target, TimeSpan delay, string methodName, params object[] args)
        {
            var hostedActor = target as IHostedActor ?? throw new InvalidOperationException("Actor must inherit from class Actor for scheduler to work.");

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
                            await hostedActor.InvokeAsync(methodName, args, ActorAddress.Local, default);
                    }
                    catch (TaskCanceledException)
                    {
                    }
                }
            }).ConfigureAwait(false);

            return cancelation;
        }

        public static ICancelable ScheduleRepeatedly(IActor target, TimeSpan frequency, string methodName, params object[] args)
        {
            var hostedActor = target as IHostedActor ?? throw new InvalidOperationException("Actor must inherit from class Actor for scheduler to work.");

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
                                await hostedActor.InvokeAsync(methodName, args, ActorAddress.Local, default);
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
