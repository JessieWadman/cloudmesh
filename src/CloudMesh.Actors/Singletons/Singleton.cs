using CloudMesh.Utils;
using Microsoft.Extensions.Hosting;

namespace CloudMesh.Actors.Singletons
{
    public abstract class Singleton : IHostedService
    {
        private static readonly TimeSpan leaseDuration = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan retryAcquireLeaseFrequency = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan updateLeaseLockFrequency = TimeSpan.FromSeconds(10);

        private readonly ISingletonLeaseProvider singletonLeaseProvider;
        private Task completion;
        protected readonly string SingletonName;
        private readonly CancellationTokenSource stoppingTokenSource = new();
        private readonly AsyncLock userDataLock = new();
        private string userData;
        private DateTime leaseLastRenewedUtc = DateTime.MinValue;
        protected readonly string InstanceId = Guid.NewGuid().ToString();

        protected Singleton(string? singletonName = null)
        {
            this.singletonLeaseProvider = SingletonLease.Instance ?? throw new InvalidOperationException("Singleton lease provider not configured. Did you forget to call services.AddSingletonLeaseProvider(...)?");
            this.SingletonName = singletonName ?? GetType().Name;
        }

        protected Task SetUserDataAsync(string userData) => SetUserDataInternalAsync(userData, true);

        private async Task SetUserDataInternalAsync(string? newUserData, bool hasNewUserData)
        {
            if (lease is null)
                throw new ObjectDisposedException("Lease has already been released!");

            using var _ = await userDataLock.LockAsync();
            if (!hasNewUserData)
                newUserData = this.userData;
            await lease!.UpdateUserDataAsync(newUserData ?? string.Empty, leaseDuration);
            this.userData = newUserData ?? string.Empty;
            this.leaseLastRenewedUtc = DateTime.UtcNow;
        }

        ISingletonLease? lease = null;

        Task IHostedService.StartAsync(CancellationToken _)
        {
            completion = Task.Run(() => RunAsync(stoppingTokenSource.Token));
            return Task.CompletedTask;
        }

        async Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            using (stoppingTokenSource)
            {
                stoppingTokenSource.Cancel();
                await completion;
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            // Repeatedly try to acquire lease until it's acquired or stoppingToken is flagged.
            while (!stoppingToken.IsCancellationRequested)
            {
                lease = await singletonLeaseProvider.TryAcquire(SingletonName, leaseDuration, stoppingToken, InstanceId);
                if (lease is not null)
                    break;
                await Task.Delay(retryAcquireLeaseFrequency, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested || lease is null)
                return;

            // Last set user data from store
            this.userData = lease.UserData;

            try
            {
                // Start user code
                Task executionTask;
                executionTask = Task.Run(() => ExecuteAsync(stoppingToken));
                try
                {
                    // Repeatedly extend the lease while user code runs, or until stoppingToken is flagged
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var timeSinceLeaseRenewed = DateTime.UtcNow - leaseLastRenewedUtc;
                        var timeRemainingOnLease = leaseDuration - timeSinceLeaseRenewed;

                        // If user code calls SetUserDataAsync above, the release will renew.
                        // If that is the case, and the remaining time on the lease is 
                        // sufficient for another wait cycle, wait until next cycle to extend
                        // the lease again. 
                        // This reduces the load on the dynamodb table when user code often
                        // calls SetUserDataAsync
                        if (timeRemainingOnLease > leaseDuration - TimeSpan.FromSeconds(3))
                        {
                            await SetUserDataInternalAsync(default, false);
                        }

                        try
                        {
                            await Task.Delay(updateLeaseLockFrequency, stoppingToken);
                        }
                        catch (TaskCanceledException)
                        {
                        }
                    }
                }
                finally
                {
                    try
                    {
                        await executionTask;
                    }
                    catch (TaskCanceledException) { }
                }
            }
            finally
            {
                await lease.UpdateUserDataAsync(userData, leaseDuration);
                await lease.ReleaseAsync();
            }
        }

        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
    }
}
