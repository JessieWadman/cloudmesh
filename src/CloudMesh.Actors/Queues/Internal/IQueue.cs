using CloudMesh.Routing;

namespace CloudMesh.Queues.Internal
{
    public interface IQueue
    {
        ValueTask SendAsync<T>(ResourceInstance route, TimeSpan? delay, Func<T, string?>? deduplicationId, T[] values, CancellationToken cancellationToken);
    }
}
