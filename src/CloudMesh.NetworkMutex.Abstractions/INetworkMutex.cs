namespace CloudMesh.NetworkMutex.Abstractions;

public interface INetworkMutex
{
    Task<INetworkMutexLock?> TryAcquireLockAsync(
        string mutexName,
        CancellationToken cancellationToken);
    
    public async Task<INetworkMutexLock?> TryAcquireLockAsync(string mutexName, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await TryAcquireLockAsync(mutexName, cts.Token);
    }
}