using System.Collections.Concurrent;

namespace CloudMesh.Routing
{
    public class MockRouteResolver : IRouteResolver
    {
        public ConcurrentDictionary<string, ConcurrentDictionary<string, ResourceInstance[]>> Resources { get; set; } = new();

        public ValueTask<ResourceInstance[]> ResolveAsync(string type, string name)
        {
            return new(Resources.GetOrAdd(type, _ => new()).GetOrAdd(name, _ => Array.Empty<ResourceInstance>()));
        }

        public void Set(string type, string name, params ResourceInstance[] resourceIdentifiers)
        {
            Resources.GetOrAdd(type, _ => new())[name] = resourceIdentifiers;
        }

        public void Set<T>(string type, params ResourceInstance[] resourceIdentifiers)
        {
            Resources.GetOrAdd(type, _ => new())[typeof(T).Name] = resourceIdentifiers;
        }
    }
}
