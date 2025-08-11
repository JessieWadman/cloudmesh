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
        ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params DynamoDBValue[] hashKeys);
        ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys);
        
        /// <summary>
        /// Attempts to save the item, only if it does not already exist.
        /// </summary>
        /// <param name="item">Item to save</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the item was created, false if it already existed</returns>
        ValueTask<bool> CreateAsync(T item, CancellationToken cancellationToken);
        
        /// <summary>
        /// Saves the item, overwriting an existing on if it exists
        /// </summary>
        /// <param name="item">Item to save</param>
        /// <param name="cancellationToken">CancellationToken</param>
        ValueTask SaveAsync(T item, CancellationToken cancellationToken);
        
        /// <summary>
        /// Saves the items, overwriting existing ones if they exist
        /// </summary>
        /// <param name="items">Items to save</param>
        /// <param name="cancellationToken">CancellationToken</param>
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
