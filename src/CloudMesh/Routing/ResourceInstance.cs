using System.Collections.Immutable;

namespace CloudMesh.Routing
{
    public record ResourceInstance(
        string InstanceId, 
        ResourceIdentifier Address, 
        ImmutableDictionary<string, string> Attributes);
}