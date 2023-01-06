using CloudMesh.Routing;

namespace CloudMesh.Queues.Internal
{
    public static class QueueProviders
    {
        public static readonly SchemeProviderRegistry<IQueue> Instance = new();        
    }
}
