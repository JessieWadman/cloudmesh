namespace CloudMesh.Routing
{
    public interface IRouteResolver
    {
        ValueTask<ResourceInstance[]> ResolveAsync(string type, string name);
    }

    public static class ResolverExtensions
    {
        public static ValueTask<ResourceInstance?> ResolveQueue(this IRouteResolver resolver, string queueName)
        {
            var instances = resolver.ResolveAsync("Queues", queueName);
            if (instances.IsCompletedSuccessfully)
                return new(instances.Result.FirstOrDefault());
            return new(Impl());

            async Task<ResourceInstance?> Impl()
            {
                var resources = await instances;
                return resources.FirstOrDefault();
            }
        }

        public static ValueTask<ResourceInstance?> ResolveTopic(this IRouteResolver resolver, string topicName)
        {
            var instances = resolver.ResolveAsync("Topics", topicName);
            if (instances.IsCompletedSuccessfully)
                return new(instances.Result.FirstOrDefault());
            return new(Impl());

            async Task<ResourceInstance?> Impl()
            {
                var resources = await instances;
                return resources.FirstOrDefault();
            }
        }

        public static ValueTask<ResourceInstance?> ResolveStore(this IRouteResolver resolver, string storeName)
        {
            var instances = resolver.ResolveAsync("Stores", storeName);
            if (instances.IsCompletedSuccessfully)
                return new(instances.Result.FirstOrDefault());
            return new(Impl());

            async Task<ResourceInstance?> Impl()
            {
                var resources = await instances;
                return resources.FirstOrDefault();
            }
        }

        public static ValueTask<ResourceInstance[]> ResolveActor<T>(this IRouteResolver resolver)
            => resolver.ResolveAsync("Actors", typeof(T).Name);

        public static ValueTask<ResourceInstance[]> ResolveService<T>(this IRouteResolver resolver)
            => resolver.ResolveAsync("Services", typeof(T).Name);
    }
}