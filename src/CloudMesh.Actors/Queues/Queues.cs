using CloudMesh.Queues.Internal;
using CloudMesh.Routing;

namespace CloudMesh.Queues
{
    public static class Queues
    {
        public static async Task PublishAsync<T>(string queueName, T message, TimeSpan? delay = null, string? deduplicationId = null, CancellationToken cancellationToken = default)
        {
            var resource = await Router.RouteResolver.ResolveQueue(queueName);

            if (resource is null)
                throw new NoRouteFoundException($"No routes found for queue {queueName}");

            if (!QueueProviders.Instance.TryGet(resource.Address.Scheme, out var queue) || queue is null)
                throw new NoProviderForSchemeException($"No provider registered for scheme {resource.Address.Scheme}");

            await queue.SendAsync(resource, delay, _ => deduplicationId, new[] { message }, cancellationToken);
        }

        public static async Task PublishAsync<T>(
            string queueName, T[] messages, 
            TimeSpan? delay = null, 
            Func<T, string?>? deduplicationIds = null, 
            CancellationToken cancellationToken = default)
        {
            var resource = await Router.RouteResolver.ResolveQueue(queueName);

            if (resource is null)
                throw new NoRouteFoundException($"No routes found for queue {queueName}");

            if (!QueueProviders.Instance.TryGet(resource.Address.Scheme, out var queue) || queue is null)
                throw new NoProviderForSchemeException($"No provider registered for scheme {resource.Address.Scheme}");

            await queue.SendAsync(resource, delay, deduplicationIds, messages, cancellationToken);
        }
    }
}
