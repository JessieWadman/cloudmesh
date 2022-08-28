namespace CloudMesh.Actors.Routing
{
    public record RoutingTableEntry(string ServiceName, string InstanceId, string IpAddress, bool IsLocal);
}
