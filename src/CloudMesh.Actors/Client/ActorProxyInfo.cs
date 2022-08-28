using CloudMesh.Actors.Routing;

namespace CloudMesh.Actors.Client
{
    public record ActorProxyInfo(
        string ServiceName,
        string ActorName,
        Type ProxyType,
        IRouter Router);
}
