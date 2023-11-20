using CloudMesh.Persistence.DynamoDB.Builders;

namespace CloudMesh.Persistence.DynamoDB
{
    public interface IRepositoryFactory
    {
        IRepository<T> For<T>(string tableName);
        ITransactWriteBuilder Transaction();
    }

    public interface IRepository<T> : IDisposable
    {
        ValueTask<T?> GetById(DynamoDBValue hashKey, CancellationToken cancellationToken);
        ValueTask<T?> GetById(DynamoDBValue hashKey, DynamoDBValue rangeKey, CancellationToken cancellationToken);
        ValueTask<T?> GetById(string indexName, DynamoDBValue hashKey, CancellationToken cancellationToken);
        ValueTask<T?> GetById(string indexName, DynamoDBValue hashKey, DynamoDBValue rangeKey, CancellationToken cancellationToken);
        ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params DynamoDBValue[] hashKeys);
        ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys);
        ValueTask<T[]> GetByIds(string indexName, CancellationToken cancellationToken, params DynamoDBValue[] hashKeys);
        ValueTask<T[]> GetByIds(string indexName, CancellationToken cancellationToken, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys);

        ValueTask SaveAsync(T item, CancellationToken cancellationToken);
        ValueTask SaveAsync(IEnumerable<T> items, CancellationToken cancellationToken);
        ValueTask DeleteAsync(T item, CancellationToken cancellationToken);
        ValueTask DeleteAsync(CancellationToken cancellationToken, params T[] items);
        ValueTask DeleteAsync(DynamoDBValue id, CancellationToken cancellationToken);
        ValueTask DeleteAsync(DynamoDBValue hashKey, DynamoDBValue sortKey, CancellationToken cancellationToken);
        IBatchWriteBuilder<T> BatchWrite();
        IQueryBuilder<T> Query();
        IScanBuilder<T> Scan();
        IPatchBuilder<T> Patch(DynamoDBValue hashKey);
        IPatchBuilder<T> Patch(DynamoDBValue hashKey, DynamoDBValue rangeKey);
    }
}
