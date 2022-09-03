using CloudMesh.Routing;

namespace CloudMesh.Queues.Internal
{
    public static class TopicProviders
    {
        public static readonly SchemeProviderRegistry<ITopic> Instance = new();
    }
}
