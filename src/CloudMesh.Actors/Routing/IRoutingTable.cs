using System.Collections.Immutable;

namespace CloudMesh.Actors.Routing
{
    public interface IRoutingTableUpdate
    {
        void RoutingTableUpdated(ImmutableArray<RoutingTableEntry> routingTable);
    }

    public interface IRoutingTable
    {
        void RegisterNotifications(IRoutingTableUpdate target);
        void UnregisterNotifications(IRoutingTableUpdate target);

        ImmutableArray<RoutingTableEntry> GetAll();
        ImmutableArray<RoutingTableEntry> GetByService(string serviceName);
        ValueTask UpdateAsync(ImmutableArray<RoutingTableEntry> newEntries);
        ValueTask<bool> WaitForInitialRoutingTableAsync(TimeSpan timeout);
    }
}
