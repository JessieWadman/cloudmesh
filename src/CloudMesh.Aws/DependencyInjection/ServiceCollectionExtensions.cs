using CloudMesh.Aws.Queues;
using CloudMesh.Aws.Remoting;
using CloudMesh.Aws.Secrets;
using CloudMesh.Aws.Singletons;
using CloudMesh.Queues.Internal;
using CloudMesh.Secrets.Internals;
using CloudMesh.Services.Internal;
using CloudMesh.Singletons.Internal;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAwsCloudMesh(this IServiceCollection services)
        {
            ServiceTransports.Instance.Register("lambda", () => LambdaInvoker.Instance);
            SecretsProviders.Instance.Register("secret", () => SecretsManagerProvider.Instance);
            QueueProviders.Instance.Register("sqs", () => SqsPublisher.Instance);

            SingletonLease.Instance = new DynamoDBSingletonLeaseStore();
            services.AddSingleton(SingletonLease.Instance);

            return services;
        }
    }
}
