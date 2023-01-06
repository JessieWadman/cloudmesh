using CloudMesh.Remoting;
using CloudMesh.Routing;
using System.Reflection;

namespace CloudMesh.Services.Internal
{
    internal static class ServiceTransportProxyRoundRobinCounter
    {
        public static long Counter;
    }

    public class ServiceTransportProxy<T> : TransportProxy<T>
    {
        protected override ValueTask<object?> InvokeAsync(ResourceIdentifier route, MethodInfo method, object?[]? arguments)
        {
            arguments ??= Array.Empty<object?>();
            if (!ServiceTransports.Instance.TryGet(route.Scheme, out var transport) || transport is null)
                throw new NoProviderForSchemeException($"No provider is registered for scheme {route.Scheme}://");
            return transport.InvokeAsync(route, method, arguments);
        }

        protected override async ValueTask<ResourceIdentifier?> TryResolveOne()
        {
            var routes = await Router.RouteResolver.ResolveService<T>();
            if (routes.Length == 0)
                return null;
            var counter = Interlocked.Increment(ref ServiceTransportProxyRoundRobinCounter.Counter);
            return routes.Select(r => r.Address).ToArray()[counter % routes.Length];
        }
    }
}
