namespace CloudMesh.Actors.Singletons
{
    public interface ISingletonLease
    {
        string SingletonName { get; }
        string UserData { get; }
        ValueTask UpdateUserDataAsync(string userData, TimeSpan leaseDuration);
        ValueTask ReleaseAsync();
    }

    public interface ISingletonLeaseProvider
    {
        ValueTask<ISingletonLease?> TryAcquire(
            string singletonName,
            TimeSpan leaseDuration,
            CancellationToken stoppingToken,
            string? instanceId = null);
    }
}