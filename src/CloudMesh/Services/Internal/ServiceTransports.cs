using CloudMesh.Remoting.Http;
using CloudMesh.Routing;

namespace CloudMesh.Services.Internal
{
    public static class ServiceTransports
    {
        public static readonly SchemeProviderRegistry<IServiceTransport> Instance = new();

        static ServiceTransports()
        {
            Instance.Register("http", () => HttpServiceTransport.Instance);
            Instance.Register("https", () => HttpServiceTransport.Instance);
        }
    }
}
