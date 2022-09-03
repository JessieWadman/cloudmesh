using CloudMesh.Actors.Hosting;
using CloudMesh.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudMesh.Actors
{
    public static class Scheduler
    {
        public static void ScheduleOnce<T>(IActor target, TimeSpan delay, string methodName, params object[] args)
        {
            var hostedActor = target as IHostedActor ?? throw new InvalidOperationException("Actor must inherit from class Actor for scheduler to work.");

            _ = Task.Run(async () =>
            {
                if (delay != TimeSpan.Zero)
                    await Task.Delay(delay);
                await hostedActor.InvokeAsync(methodName, args, ActorAddress.Local, false, default);
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
                            await hostedActor.InvokeAsync(methodName, args, ActorAddress.Local, false, default);
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
                        var nextCall = DateTime.Now + frequency;
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            var remainingTime = nextCall - DateTime.Now;
                            await Task.Delay(remainingTime, stoppingToken);
                            if (!stoppingToken.IsCancellationRequested)
                            {
                                // We wait for completion to prevent overlapping calls in buffered mailboxes that might
                                // queue up lots of requests and not be able to keep up
                                await hostedActor.InvokeAsync(methodName, args, ActorAddress.Local, true, default);
                                nextCall += frequency;
                            }
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
