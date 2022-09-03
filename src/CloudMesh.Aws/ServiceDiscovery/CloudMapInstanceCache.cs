using Amazon;
using Amazon.ServiceDiscovery;
using CloudMesh.Routing;
using CloudMesh.Utils;
using System.Collections.Immutable;

namespace CloudMesh.Aws.ServiceDiscovery
{
    public class CloudMapInstanceCache
    {
        private readonly AmazonServiceDiscoveryClient client;
        private readonly string cloudMapNamespace;
        private readonly string serviceName;
        private readonly HashSet<ResourceInstance> cache = new();
        private bool initialCompletion = false;
        private readonly TaskCompletionSource<ResourceInstance[]> initialCompletionSource = new();
        private readonly AsyncLock locker = new();

        public CloudMapInstanceCache(AmazonServiceDiscoveryClient client, string cloudMapNamespace, string serviceName)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.cloudMapNamespace = cloudMapNamespace ?? throw new ArgumentNullException(nameof(cloudMapNamespace));
            this.serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
            RefreshCache().ConfigureAwait(false);
        }

        public ValueTask<ResourceInstance[]> GetInstances()
        {
            lock (cache)
            {
                if (initialCompletion)
                    return new(cache.ToArray());               
            }
            return new(initialCompletionSource.Task);
        }

        public async Task RefreshCache()
        {
            using var _ = locker.LockAsync();

            var query = await client.DiscoverInstancesAsync(new()
            {
                NamespaceName = cloudMapNamespace,
                ServiceName = serviceName,
                HealthStatus = Amazon.ServiceDiscovery.HealthStatus.HEALTHY.Value,
            });

            var instances = new List<ResourceInstance>();
            foreach (var instance in query.Instances)
            {
                if (instance.Attributes.TryGetValue("AWS_INSTANCE_IPV4", out var ip))
                {
                    if (!instance.Attributes.TryGetValue("AWS_INSTANCE_PORT", out var port))
                        port = "3500";
                    instances.Add(new ResourceInstance(instance.InstanceId, new ResourceIdentifier("http", $"{ip}:{port}"), instance.Attributes.ToImmutableDictionary()));
                }
                else if (instance.Attributes.TryGetValue("Arn", out var arnString))
                {
                    var arn = Arn.Parse(arnString);
                    var resource = arn.Resource.Split(':').Last();
                    instances.Add(new ResourceInstance(instance.InstanceId, new ResourceIdentifier(arn.Service, resource), instance.Attributes.ToImmutableDictionary()));
                }
                else if (instance.Attributes.TryGetValue("ServiceName", out var serviceName))
                {
                    instances.Add(new ResourceInstance(instance.InstanceId, new ResourceIdentifier("service", serviceName), instance.Attributes.ToImmutableDictionary()));
                }
            }

            lock (cache)
            {
                cache.Clear();
                foreach (var instance in instances)
                    cache.Add(instance);
                if (!initialCompletion)
                {
                    initialCompletion = true;
                    initialCompletionSource.SetResult(cache.ToArray());
                }
            }            
        }

        public ValueTask<ResourceInstance?> GetById(string instanceId)
        {
            var call = GetInstances();
            if (call.IsCompletedSuccessfully)
                return new(call.Result.Where(i => i.InstanceId == instanceId).FirstOrDefault());

            return new(AwaitAndReturn());

            async Task<ResourceInstance?> AwaitAndReturn()
            {
                var instances = await call;
                return instances.Where(i => i.InstanceId == instanceId).FirstOrDefault();
            }
        }
    }
}