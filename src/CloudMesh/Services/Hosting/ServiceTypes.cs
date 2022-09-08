using System.Collections.Concurrent;

namespace CloudMesh.Services.Hosting
{
    public static class ServiceTypes
    {
        public static readonly ConcurrentDictionary<string, Type> ServiceNamesToTypes = new();
    }
}
