using CloudMesh.Actors.Utils;

namespace CloudMesh.Actors.Routing
{
    public record ActorLocation(bool IsLocal, string Address);

    public interface IRouter
    {
        ActorLocation Resolve(string serviceName, string actorName, string id);
        bool TryResolve(string serviceName, string actorName, string id, out ActorLocation? location);
        ValueTask<ActorLocation?> TryResolveAsync(string serviceName, string actorName, string id, TimeSpan timeout);
    }

    public abstract class Router : IRouter
    {
        protected readonly IRoutingTable RoutingTable;

        protected Router(IRoutingTable routingTable)
        {
            this.RoutingTable = routingTable ?? throw new ArgumentNullException(nameof(routingTable));
        }

        public abstract bool TryResolve(string serviceName, string actorName, string id, out ActorLocation? location);

        public ActorLocation Resolve(string serviceName, string actorName, string id)
        {
            if (!TryResolve(serviceName, actorName, id, out var location) || location is null)
                throw new RoutingException($"There are no nodes for service {serviceName}");
            return location;
        }

        public ValueTask<ActorLocation?> TryResolveAsync(string serviceName, string actorName, string id, TimeSpan timeout)
        {
            var started = DateTime.UtcNow;
            var routingTableTask = RoutingTable.WaitForInitialRoutingTableAsync(timeout);
            // If we don't need to wait for routing table, go straight ahead to initial resolve try
            if (routingTableTask.IsCompleted)
                return TryOnceAndThenRetry();
            // Wait for routing table, and then do first resolve try
            return new(WaitForRoutingTableAndThenTry());

            async Task<ActorLocation?> WaitForRoutingTableAndThenTry()
            {
                var haveRoutingTable = await routingTableTask;
                if (!haveRoutingTable)
                    return null;
                return await TryOnceAndThenRetry();
            }

            ValueTask<ActorLocation?> TryOnceAndThenRetry()
            {
                if (routingTableTask.IsCompleted)
                    return new(TryResolve(serviceName, actorName, id, out var location) ? location : null);
                return new(RetryResolve());
            }

            async Task<ActorLocation?> RetryResolve()
            {
                var timeRemaining = started + timeout - DateTime.UtcNow;
                var retryCount = Convert.ToInt32(Math.Ceiling(timeRemaining.TotalMilliseconds / 250));
                if (retryCount < 1)
                    retryCount = 1;

                for (var retry = 0; retry < retryCount; retry++)
                {
                    await Task.Delay(250);
                    if (TryResolve(serviceName, actorName, id, out var location))
                        return location;
                }
                return null;
            }
        }
    }

    public class ConsistentHashRouter : Router
    {
        public ConsistentHashRouter(IRoutingTable routingTable)
            : base(routingTable)
        {
        }

        public override bool TryResolve(string serviceName, string actorName, string id, out ActorLocation? location)
        {
            location = null;
            var consistentHash = MurmurHash2.HashString($"{serviceName}/{actorName}/{id}");
            var nodes = RoutingTable.GetByService(serviceName);
            if (nodes.Length == 0)
                return false;
            var nodeId = Convert.ToInt32(consistentHash % Convert.ToUInt64(nodes.Length));
            var node = nodes[nodeId];
            location = new(node.IsLocal, node.IpAddress);
            return true;
        }
    }

    public class RoundRobinRouter : Router
    {
        private long counter = 0;

        public RoundRobinRouter(IRoutingTable routingTable)
            : base(routingTable)
        {
        }

        public override bool TryResolve(string serviceName, string actorName, string id, out ActorLocation? location)
        {
            location = null;
            var nodes = RoutingTable.GetByService(serviceName);
            if (nodes.Length == 0)
                return false;
            var nodeIndex = Interlocked.Increment(ref counter) % nodes.Length;
            var nodeId = Convert.ToInt32(nodeIndex);
            var node = nodes[nodeId];
            location = new(node.IsLocal, node.IpAddress);
            return true;
        }        
    }
}
