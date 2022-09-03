using CloudMesh.Routing;

namespace CloudMesh.Secrets.Internals
{
    public interface ISecretsProvider
    {
        ValueTask<string?> GetAsync(ResourceInstance resource, CancellationToken cancellationToken);
    }
}
