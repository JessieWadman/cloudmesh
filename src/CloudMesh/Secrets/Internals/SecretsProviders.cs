using CloudMesh.Routing;

namespace CloudMesh.Secrets.Internals
{
    public static class SecretsProviders
    {
        public static readonly SchemeProviderRegistry<ISecretsProvider> Instance = new();
    }
}
