using CloudMesh.Routing;

namespace CloudMesh.Actors
{
    public static class ActorAddress
    {
        public static readonly ResourceIdentifier Local = new("http", LocalIpAddressResolver.Instance.Resolve());
    }
}
