using CloudMesh.Remoting.Http;
using CloudMesh.Routing;

namespace CloudMesh.Actors.Internal
{
    public static class ActorTransports
    {
        public static readonly SchemeProviderRegistry<IActorTransport> Instance = new();

        static ActorTransports()
        {
            Instance.Register("http", () => HttpActorTransport.Instance);
            Instance.Register("https", () => HttpActorTransport.Instance);
        }
    }
}
