using Amazon.ServiceDiscovery;
using CloudMesh.Routing;
using System.Collections.Concurrent;

namespace CloudMesh.Aws.ServiceDiscovery
{
    public class CloudMapServiceDiscovery : IRouteResolver, IAsyncDisposable, IDisposable
    {
        private readonly Task completion;
        private readonly CancellationTokenSource stoppingTokenSource = new();
        private readonly AmazonServiceDiscoveryClient client = new();
        private readonly string cloudMapNamespace;
        private readonly ConcurrentDictionary<string, CloudMapInstanceCache> typesToResources = new();        
        private readonly ConcurrentDictionary<string, CloudMapInstanceCache> resourcesToInstances = new();

        public CloudMapServiceDiscovery(string cloudMapNamespace)
        {
            Router.RouteResolver = this;
            completion = Task.Factory.StartNew(() => RunAsync(stoppingTokenSource.Token));
            this.cloudMapNamespace = cloudMapNamespace ?? throw new ArgumentNullException(nameof(cloudMapNamespace));
        }

        private CloudMapInstanceCache GetCache(string type)
        {
            return typesToResources.GetOrAdd(type, _ => new(client, cloudMapNamespace, type));
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            stoppingTokenSource.Cancel();
            await completion;
            client.Dispose();
        }

        public ValueTask<ResourceInstance[]> ResolveAsync(string type, string name)
        {
            var getInstanceById = GetCache(type).GetById(name);
            if (getInstanceById.IsCompletedSuccessfully)
                return ResolveInner(getInstanceById.Result);
            return new(AwaitAndResolve());

            async Task<ResourceInstance[]> AwaitAndResolve()
            {
                var result = await getInstanceById;
                return await ResolveInner(result);
            }

            ValueTask<ResourceInstance[]> ResolveInner(ResourceInstance? instance)
            {
                if (instance is null)
                    return new(Array.Empty<ResourceInstance>());

                if (instance.Address.Scheme != "service")
                    return new(new[] { instance });

                var instanceCache = resourcesToInstances.GetOrAdd(instance.Address.Resource, s => new(client, cloudMapNamespace, s));
                var getInstancesByType = instanceCache.GetInstances();
                if (getInstancesByType.IsCompletedSuccessfully)
                    return new(getInstancesByType.Result);
                return new(Inner(getInstancesByType));
            }

            async Task<ResourceInstance[]> Inner(ValueTask<ResourceInstance[]> getInstancesByType)
            {
                return await getInstancesByType;
            }
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            await Task.WhenAll(
                GetCache("Actors").RefreshCache(),
                GetCache("Services").RefreshCache(),
                GetCache("Queues").RefreshCache(),
                GetCache("Topics").RefreshCache());

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            try
            {
                long iterCount = 0;
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    // These we only refresh once every minute
                    if (iterCount == 0 || iterCount % 10 == 0)
                    {
                        var tasks = typesToResources.Values.Select(c => c.RefreshCache()).ToArray();
                        await Task.WhenAll(tasks);
                    }

                    // We refresh these frequently
                    await Task.WhenAll(resourcesToInstances.Values.Select(i => i.RefreshCache()));

                    iterCount++;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}