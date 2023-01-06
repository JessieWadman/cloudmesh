using CloudMesh.Remoting;
using CloudMesh.Routing;
using CloudMesh.Utils;
using System.Reflection;

namespace CloudMesh.Actors.Internal
{
    public class ActorTransportProxy<T> : TransportProxy<T>
    {
        public string Id = string.Empty;

        protected override ValueTask<object?> InvokeAsync(ResourceIdentifier route, MethodInfo method, object?[]? arguments)
        {
            arguments ??= Array.Empty<object?>();
            if (!ActorTransports.Instance.TryGet(route.Scheme, out var transport) || transport is null)
                throw new NoProviderForSchemeException($"No transport registered for scheme {route.Scheme}://");

            return transport.InvokeAsync(route, Id, method, arguments);
        }

        protected override async ValueTask<ResourceIdentifier?> TryResolveOne()
        {
            var idHash = MurmurHash.StringHash(Id);
            var routes = await Router.RouteResolver.ResolveActor<T>();
            if (routes.Length == 0)
                return null;
            return routes.Select(r => r.Address).ToArray()[idHash % routes.Length];
        }
    }
}
