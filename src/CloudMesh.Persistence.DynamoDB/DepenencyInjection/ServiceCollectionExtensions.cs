using Amazon.DynamoDBv2;
using Amazon.Runtime;
using CloudMesh.Persistence.DynamoDB;

namespace Microsoft.Extensions.DependencyInjection
{
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
