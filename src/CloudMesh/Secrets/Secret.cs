using CloudMesh.Routing;
using CloudMesh.Secrets.Internals;

namespace CloudMesh.Secrets
{
    public static class Secret
    {
        public static ValueTask<string?> GetAsync(string name, CancellationToken cancellationToken)
        {
            var resolveSecret = Router.RouteResolver.ResolveAsync("Secrets", name);
            if (resolveSecret.IsCompletedSuccessfully)
                return Get(resolveSecret.Result);
            return new(AwaitAndGet());

            async Task<string?> AwaitAndGet()
            {
                var resource = await resolveSecret;
                return await Get(resource);
            }

            ValueTask<string?> Get(ResourceInstance[] resources)
            {
                if (resources.Length == 0)
                    throw new NoRouteFoundException($"No route found for secret {name}");

                var resource = resources[0];

                if (!SecretsProviders.Instance.TryGet(resource.Address.Scheme, out var provider) || provider is null)
                    throw new NoProviderForSchemeException($"No provider registered for scheme {resource.Address.Scheme}://");

                return provider.GetAsync(resource, cancellationToken);
            }
        }
    }
}
