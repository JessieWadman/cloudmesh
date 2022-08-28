using Amazon.ServiceDiscovery;
using Amazon.ServiceDiscovery.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace CloudMesh.Actors.Routing.Ecs
{
    public class CloudMapRouterTableUpdater : BackgroundService
    {
        public static string CloudMapNamespace { get; set; }
        private readonly IRoutingTable routingTable;
        private readonly ILogger<CloudMapRouterTableUpdater> logger;

        public CloudMapRouterTableUpdater(IRoutingTable routingTable, ILogger<CloudMapRouterTableUpdater> logger)
        {
            this.routingTable = routingTable ?? throw new ArgumentNullException(nameof(routingTable));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private async Task<RoutingTableEntry[]> DiscoverInstancesAsync(AmazonServiceDiscoveryClient client, string serviceName)
        {
            var instances = await client.DiscoverInstancesAsync(new DiscoverInstancesRequest
            {
                NamespaceName = CloudMapNamespace,
                ServiceName = serviceName
            });

            var result = new List<RoutingTableEntry>();

            foreach (var instance in instances.Instances)
            {
                if (!instance.Attributes.TryGetValue("AWS_INSTANCE_IPV4", out var ipAddress))
                {
                    logger.LogDebug($"Router - Ignoring instance {instance.InstanceId} from service {instance.ServiceName} because no IP was found");
                }
                else
                {
                    if (!instance.Attributes.TryGetValue("PORT", out var portValue) || !int.TryParse(portValue, out var port))
                        port = 5000;

                    logger.LogDebug($"Found instance {instance.InstanceId} for service {instance.ServiceName} on IP {ipAddress}");
                    var isLocal = LocalIpAddressResolver.Instance.Resolve() == ipAddress;
                    result.Add(new(instance.ServiceName, instance.InstanceId, $"{ipAddress}:{port}", isLocal));
                }
            }
            return result.ToArray();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    logger.LogDebug("Refreshing service instances...");

                    var client = new AmazonServiceDiscoveryClient();

                    string? nextToken = null;
                    var lastDiscover = 0;
                    var entries = new List<RoutingTableEntry>();

                    do
                    {
                        var services = await client.ListServicesAsync(new ListServicesRequest { NextToken = nextToken });

                        logger.LogDebug($"Found {services.Services.Count} services");

                        var discoverTasks = services.Services.Select(svc => DiscoverInstancesAsync(client, svc.Name)).ToArray();
                        var allEntries = await Task.WhenAll(discoverTasks);
                        var entriesPage = allEntries.SelectMany(e => e).ToArray();
                        entries.AddRange(entriesPage);
                        lastDiscover = entriesPage.Length;
                    }
                    while (nextToken is not null && lastDiscover > 0);

                    await routingTable.UpdateAsync(entries.ToImmutableArray());
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, ex.ToString());
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
