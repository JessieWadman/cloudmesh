using Amazon.DynamoDBv2;
using CloudMesh.Persistence.DynamoDB.Builders;

namespace CloudMesh.Persistence.DynamoDB
{
    /// <summary>
    /// The production <see cref="IRepositoryFactory"/> that creates repositories and transactions backed by a
    /// real <see cref="IAmazonDynamoDB"/> client. Registered by <c>AddDynamoDBPersistence</c>.
    /// </summary>
    public class DynamoDBRepositoryFactory : IRepositoryFactory
    {
        private readonly IAmazonDynamoDB dynamoDB;

        /// <summary>Creates the factory over the given DynamoDB client.</summary>
        /// <param name="dynamoDB">The AWS DynamoDB client used by all repositories this factory creates.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dynamoDB"/> is <see langword="null"/>.</exception>
        public DynamoDBRepositoryFactory(IAmazonDynamoDB dynamoDB)
        {
            this.dynamoDB = dynamoDB ?? throw new ArgumentNullException(nameof(dynamoDB));
        }

        public IRepository<T> For<T>(string tableName)
        {
            return new DynamoDBRepository<T>(tableName, dynamoDB);
        }

        public ITransactWriteBuilder Transaction()
        {
            return new TransactWriteBuilder(dynamoDB);
        }
    }
}
