using Amazon.ServiceDiscovery;
using Proto;
using Proto.Cluster;
using Proto.Utils;

namespace CartServices.Helpers
{
    public record CloudMapAutoRegisteredServiceEntry(string CloudMapNamespace, string CloudMapServiceName, int DefaultPort, string[] Kinds);

    /// <summary>
    /// This provider relies on ECS automatically registering and deregistering us in CloudMap.
    /// It has the benefit of highly reliable lifetime management of the registration.
    /// It has the drawback of not providing a lot of app-specific metadata, such as port and supported kinds, so we
    /// must provide them manually. This -can- be accomplished using ECS agent on the Task, but is finicky.
    /// </summary>
    internal class AmazonAutoRegisteredCloudMapProvider : IClusterProvider
    {
        private static readonly ILogger Logger = Log.CreateLogger<AmazonAutoRegisteredCloudMapProvider>();

        private string address;
        private Cluster cluster;

        private string clusterName;
        private string host;
        private string[] kinds;
        private MemberList memberList;
        private int port;
        private readonly AmazonCloudMapProviderOptions options;
        private readonly AmazonServiceDiscoveryClient client;
        private readonly SortedSet<CloudMapAutoRegisteredServiceEntry> services = new();

        public AmazonAutoRegisteredCloudMapProvider(
            CloudMapAutoRegisteredServiceEntry[] services,
            AmazonCloudMapProviderOptions? options = null,
            AmazonServiceDiscoveryClient? client = null)
        {
            foreach (var reg in services)
                this.services.Add(reg);

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

        // ECS will automatically register the member
        public Task RegisterMemberInner() => Task.CompletedTask;

        private async Task<Member[]> GetMembers()
        {
            var queries = services.Select(s => new
            {
                Kinds = s.Kinds,
                DefaultPort = s.DefaultPort,
                Task = client.DiscoverInstancesAsync(new()
                {
                    NamespaceName = s.CloudMapNamespace,
                    ServiceName = s.CloudMapServiceName
                })
            }).ToArray();

            await Task.WhenAll(queries.Select(t => t.Task));


            var map = from m in queries
                            from i in m.Task.Result.Instances
                            select new Member()
                            {
                                Id = i.InstanceId,
                                Host = i.Attributes.TryGetValue("AWS_INSTANCE_IPV4", out var address) ? address : string.Empty,
                                // Port is only configured if task is running ECS agent
                                Port = i.Attributes.TryGetValue("AWS_INSTANCE_PORT", out var portString) && int.TryParse(portString, out var port)
                                    ? port
                                    : m.DefaultPort,
                                Kinds = { m.Kinds }
                            };

            var instances = map.ToArray();
            return instances;
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

        // ECS will automatically deregister the member. Nothing to do
        private Task DeregisterMemberInner() => Task.CompletedTask;
    }
}
