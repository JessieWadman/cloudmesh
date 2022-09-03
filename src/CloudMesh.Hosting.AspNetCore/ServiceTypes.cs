using System.Collections.Concurrent;

namespace CloudMesh.Hosting.AspNetCore
{
    public static class ServiceTypes
    {
        public static readonly ConcurrentDictionary<string, Type> ServiceNamesToTypes = new();
    }
}
