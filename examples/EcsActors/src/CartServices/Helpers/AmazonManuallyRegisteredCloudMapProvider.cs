using Amazon.ServiceDiscovery;
using Amazon.ServiceDiscovery.Model;
using Proto;
using Proto.Cluster;
using Proto.Cluster.AmazonECS;
using Proto.Utils;

namespace CartServices.Helpers
{
    /// <summary>
    /// This provider will manually register and deregister us as an instance in CloudMap.
    /// It has the benefit of automatically discovering ports and kinds (less manual config, better isolation of concern).
    /// It has the drawback of not having as robust lifetime management as the one managed by ECS.
    /// </summary>
    internal class AmazonManuallyRegisteredCloudMapProvider : IClusterProvider
    {
        private static readonly ILogger Logger = Log.CreateLogger<AmazonManuallyRegisteredCloudMapProvider>();

        private string address;
        private Cluster cluster;

        private string clusterName;
        private string host;
        private string[] kinds;
        private MemberList memberList;
        private int port;
        private readonly string cloudMapNamespaceName;
        private readonly string cloudMapServiceName;
        private readonly AmazonCloudMapProviderOptions options;
        private readonly AmazonServiceDiscoveryClient client;

        public AmazonManuallyRegisteredCloudMapProvider(
            string cloudMapNamespaceName,
            string cloudMapServiceName,
            AmazonCloudMapProviderOptions? options = null,
            AmazonServiceDiscoveryClient? client = null)
        {
            this.cloudMapNamespaceName = cloudMapNamespaceName ?? throw new ArgumentNullException(nameof(cloudMapNamespaceName));
            this.cloudMapServiceName = cloudMapServiceName ?? throw new ArgumentNullException(nameof(cloudMapServiceName));
            this.options = options ?? new();
            this.client = client ?? new();
        }

        public async Task StartMemberAsync(Cluster cluster)
        {
            var memberList = cluster.MemberList;
            var clusterName = cluster.Config.ClusterName;
            var (host, port) = cluster.System.GetAddress();
            var kinds = cluster.GetClusterKinds();
            this.cluster = cluster;
            this.memberList = memberList;
            this.clusterName = clusterName;
            this.host = host;
            this.port = port;
            this.kinds = kinds;
            address = host + ":" + port;
            StartClusterMonitor();
            await RegisterMemberAsync();
        }

        public Task StartClientAsync(Cluster cluster)
        {
            var memberList = cluster.MemberList;
            var clusterName = cluster.Config.ClusterName;
            var (host, port) = cluster.System.GetAddress();
            this.cluster = cluster;
            this.memberList = memberList;
            this.clusterName = clusterName;
            this.host = host;
            this.port = port;
            kinds = Array.Empty<string>();
            StartClusterMonitor();
            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(bool graceful) => await DeregisterMemberAsync();

        public async Task RegisterMemberAsync()
        {
            await Retry.Try(RegisterMemberInner, onError: OnError, onFailed: OnFailed, retryCount: Retry.Forever);

            static void OnError(int attempt, Exception exception) => Logger.LogWarning(exception, "Failed to register service");

            static void OnFailed(Exception exception) => Logger.LogError(exception, "Failed to register service");
        }

        private string? _serviceId;

        private ValueTask<string?> GetServiceId()
        {
            if (_serviceId is not null)
                return new(_serviceId);

            return new(Inner());

            async Task<string?> Inner()
            {
                var services = await client.ListServicesAsync(new ListServicesRequest());
                _serviceId = services.Services
                    .Where(s => s.Name.Equals(this.cloudMapServiceName, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Id)
                    .FirstOrDefault();

                if (_serviceId is null)
                {
                    Logger.LogError("The specified service does not exist in CloudMap!");
                    return null;
                }

                return _serviceId;
            }
        }
    
        public async Task RegisterMemberInner()
        {
            Logger.LogInformation("[Cluster][AmazonEcsProvider] Registering service on {PodIp}", address);

            var tags = new Dictionary<string, string>
            {
                [ProtoLabels.LabelCluster] = clusterName,
                [ProtoLabels.LabelPort] = port.ToString(),
                ["cluster.proto.actor/ipv4"] = address.Split(':').First(),
                [ProtoLabels.LabelMemberId] = cluster.System.Id
            };

            foreach (var kind in kinds)
            {
                var labelKey = $"{ProtoLabels.LabelKind}-{kind}";
                tags.TryAdd(labelKey, "true");
            }

            try
            {
                var serviceId = await GetServiceId();
                if (serviceId is null)
                {
                    // Throw?
                    return;
                }

                await client.RegisterInstanceAsync(new RegisterInstanceRequest
                {
                    ServiceId = _serviceId,
                    InstanceId = cluster.System.Id,
                    Attributes = tags,
                    CreatorRequestId = Guid.NewGuid().ToString()
                });
            }
            catch (Exception x)
            {
                Logger.LogError(x, "Failed to update metadata");
            }
        }

        private async Task<Member[]> GetMembers()
        {
            var instances = await client.DiscoverInstancesAsync(new()
            {
                NamespaceName = cloudMapNamespaceName,
                ServiceName = cloudMapServiceName
            });

            var members = new List<Member>();
            foreach (var instance in instances.Instances)
            {
                var kinds = instance.Attributes
                    .Where(kvp => kvp.Key.StartsWith(ProtoLabels.LabelKind))
                    .Select(kvp => kvp.Key[(ProtoLabels.LabelKind.Length + 1)..]).ToArray();

                if (kinds.Length == 0)
                {
                    Logger.LogWarning("Skipping Instance {instanceId}, no Proto Tags found for Kind", instance.InstanceId);
                    continue;
                }

                if (!instance.Attributes.TryGetValue("cluster.proto.actor/ipv4", out var ipv4Address))
                {
                    Logger.LogWarning("Skipping instance {instanceId}, no IP address registered", instance.InstanceId);
                }

                // Try get port from SRV record
                if (!instance.Attributes.TryGetValue("AWS_INSTANCE_PORT", out var portString) || !int.TryParse(portString, out var port))
                {
                    // Fall back to custom attribute
                    if (!instance.Attributes.TryGetValue(ProtoLabels.LabelPort, out portString) || !int.TryParse(portString, out port))
                        // Fall back to default port
                        port = 3500;
                }

                var member = new Member
                {
                    Id = instance.InstanceId,
                    Port = port,
                    Host = ipv4Address,
                    Kinds = { kinds }
                };

                members.Add(member);
            }

            return members.ToArray();
        }

        private void StartClusterMonitor()
        {
            _ = SafeTask.Run(async () =>
            {

                while (!cluster.System.Shutdown.IsCancellationRequested)
                {
                    Logger.Log(options.DebugLogLevel, "Calling ServiceDiscovery API");

                    try
                    {
                        var members = await GetMembers();

                        if (members != null)
                        {
                            Logger.Log(options.DebugLogLevel, "Got members {Members}", members.Length);
                            cluster.MemberList.UpdateClusterTopology(members);
                        }
                        else
                        {
                            Logger.LogWarning("Failed to get members from CloudMap");
                        }
                    }
                    catch (Exception x)
                    {
                        Logger.LogError(x, "Failed to get members from CloudMap");
                    }

                    await Task.Delay(options.PollIntervalSeconds);
                }
            });
        }

        public async Task DeregisterMemberAsync()
        {
            await Retry.Try(DeregisterMemberInner, onError: OnError, onFailed: OnFailed);

            static void OnError(int attempt, Exception exception) => Logger.LogWarning(exception, "Failed to deregister service");

            static void OnFailed(Exception exception) => Logger.LogError(exception, "Failed to deregister service");
        }

        private async Task DeregisterMemberInner()
        {
            Logger.LogInformation("[Cluster][AmazonEcsProvider] Unregistering service on {PodIp}", address);
            
            var serviceId = await GetServiceId();
            if (serviceId is not null)
            {
                await client.DeregisterInstanceAsync(new DeregisterInstanceRequest
                {
                    ServiceId = await GetServiceId(),
                    InstanceId = cluster.System.Id
                });
            }
        }
    }
}
