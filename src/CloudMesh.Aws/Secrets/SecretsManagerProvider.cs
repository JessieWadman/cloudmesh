using Amazon.SecretsManager;
using CloudMesh.Routing;
using CloudMesh.Secrets.Internals;

namespace CloudMesh.Aws.Secrets
{
    public class SecretsManagerProvider : ISecretsProvider
    {
        public static readonly SecretsManagerProvider Instance = new();
        public async ValueTask<string?> GetAsync(ResourceInstance resource, CancellationToken cancellationToken)
        {
            using var client = new AmazonSecretsManagerClient();
            var response = await client.GetSecretValueAsync(new()
            {
                SecretId = resource.Address.Resource
            }, cancellationToken);
            return response.SecretString;
        }
    }
}
