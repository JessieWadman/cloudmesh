using Amazon.DynamoDBv2;

namespace CloudMesh.Persistence.DynamoDB
{
    public class DynamoDBRepositoryFactory : IRepositoryFactory
    {
        private readonly IAmazonDynamoDB dynamoDB;

        public DynamoDBRepositoryFactory(IAmazonDynamoDB dynamoDB)
        {
            this.dynamoDB = dynamoDB ?? throw new ArgumentNullException(nameof(dynamoDB));
        }

        public IRepository<T> For<T>(string tableName)
        {
            return new DynamoDBRepository<T>(tableName, dynamoDB);
        }
    }
}
