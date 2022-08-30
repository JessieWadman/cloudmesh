using CloudMesh.Actors.Aws.Singletons;
using CloudMesh.Actors.Singletons;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class DynamoDBLeaseProviderRegistrationExtensions
    {
        public static IServiceCollection AddDynamoDBSingletonStore(this IServiceCollection services, string tableName)
        {
            SingletonLease.Instance = new DynamoDBSingletonLeaseStore(tableName);
            services.AddSingleton(SingletonLease.Instance);
            return services;
        }
    }
}
