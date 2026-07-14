using Amazon.DynamoDBv2;
using Amazon.Runtime;
using CloudMesh.Persistence.DynamoDB;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Dependency-injection helpers for registering the DynamoDB repository stack.
    /// </summary>
    public static class ServiceCollectionPersistenceExtensions
    {
        private static bool UseLocal()
        {
            var useLocalDynamoDBSetting = Environment.GetEnvironmentVariable("USE_LOCAL_DYNAMODB");
            if (useLocalDynamoDBSetting is null)
                return false;

            if (useLocalDynamoDBSetting.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                useLocalDynamoDBSetting.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                useLocalDynamoDBSetting.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;
            
            return false;
        }

        /// <summary>
        /// Registers <see cref="IAmazonDynamoDB"/> and <see cref="CloudMesh.Persistence.DynamoDB.IRepositoryFactory"/>
        /// (as scoped services) so repositories can be resolved via DI. When the environment variable
        /// <c>USE_LOCAL_DYNAMODB</c> is set to a truthy value, the client is pointed at a local DynamoDB endpoint
        /// (<c>http://localhost:8000</c>) for development.
        /// </summary>
        /// <param name="services">The service collection to add registrations to.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        public static IServiceCollection AddDynamoDBPersistence(this IServiceCollection services)
        {
            if (UseLocal())
            {
                services.AddScoped<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient(
                    new BasicAWSCredentials("local", "local"), new AmazonDynamoDBConfig { ServiceURL = "http://localhost:8000" }));
            }
            else
                services.AddScoped<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
            
            services.AddScoped<IRepositoryFactory, DynamoDBRepositoryFactory>();            
            
            return services;
        }
    }
}
