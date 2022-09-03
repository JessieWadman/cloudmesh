namespace CloudMesh.Singletons.Internal
{
    public interface ISingletonLeaseProvider
    {
        ValueTask<ISingletonLease?> TryAcquire(
            string singletonName,
            TimeSpan leaseDuration,
            CancellationToken stoppingToken,
            string? instanceId = null);
    }
}
