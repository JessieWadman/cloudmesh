using Microsoft.Extensions.Hosting;
using System.Collections.Immutable;

namespace CloudMesh.Actors.Routing.Testing
{
    public class TestRoutingTableUpdater : BackgroundService
    {
        private readonly IRoutingTable routingTable;

        public TestRoutingTableUpdater(IRoutingTable routingTable)
        {
            this.routingTable = routingTable ?? throw new ArgumentNullException(nameof(routingTable));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var localIp = LocalIpAddressResolver.Instance.Resolve();
            while (localIp is null)
            {
                await Task.Delay(10);
                localIp = LocalIpAddressResolver.Instance.Resolve();
            }

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await routingTable.UpdateAsync(GetAll());
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        public ImmutableArray<RoutingTableEntry> GetAll()
        {
            var localIp = LocalIpAddressResolver.Instance.Resolve();

            var addresses = new[] {
                new RoutingTableEntry("CartService", "1", "localhost:59085", false),
                new RoutingTableEntry("CartService", "2", "localhost:55033", true)
            };
            for (var i = 0; i < addresses.Length; i++)
                addresses[i] = addresses[i] with { IsLocal = addresses[i].IpAddress != localIp };

            return ImmutableArray<RoutingTableEntry>.Empty
                .Add(addresses[0])
                .Add(addresses[1]);
        }
    }
}
