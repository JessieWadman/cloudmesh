namespace CloudMesh.NetworkMutex.Abstractions;

public interface INetworkMutexLock : IAsyncDisposable
{
    public string Id { get; }
}