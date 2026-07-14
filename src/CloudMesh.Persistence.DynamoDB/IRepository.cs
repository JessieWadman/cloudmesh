using CloudMesh.Persistence.DynamoDB.Builders;

namespace CloudMesh.Persistence.DynamoDB
{
    /// <summary>
    /// Entry point for obtaining repositories and cross-table transactions. Register the DynamoDB-backed
    /// implementation with <c>services.AddDynamoDBPersistence()</c>, or use
    /// <see cref="Mock.InMemoryRepositoryFactory"/> in unit tests.
    /// </summary>
    public interface IRepositoryFactory
    {
        /// <summary>
        /// Returns a repository over the given table for the entity type <typeparamref name="T"/>. The type's
        /// DynamoDB mapping (hash/range keys, indexes) is taken from its <c>[DynamoDB*]</c> attributes.
        /// </summary>
        /// <typeparam name="T">The mapped entity type stored in the table.</typeparam>
        /// <param name="tableName">The DynamoDB table name to operate on.</param>
        IRepository<T> For<T>(string tableName);

        /// <summary>
        /// Begins a multi-item, multi-table write transaction (backed by DynamoDB <c>TransactWriteItems</c>).
        /// All operations succeed or fail atomically.
        /// </summary>
        ITransactWriteBuilder Transaction();
    }

    /// <summary>
    /// A lightweight repository over a single DynamoDB table for entity type <typeparamref name="T"/>. Offers
    /// key lookups, conditional creates/saves/deletes, batch writes, and fluent
    /// <see cref="Query"/>/<see cref="Scan"/>/<see cref="Patch(DynamoDBValue)"/> builders. A drop-in in-memory
    /// implementation (<see cref="Mock.InMemoryRepository{T}"/>) mirrors this contract for unit tests.
    /// </summary>
    /// <typeparam name="T">The mapped entity type stored in the table.</typeparam>
    public interface IRepository<T> : IDisposable
    {
        /// <summary>Fetches a single item by its hash (partition) key, or <see langword="null"/> if not found.</summary>
        /// <param name="hashKey">The partition key value.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<T?> GetById(DynamoDBValue hashKey, CancellationToken cancellationToken);

        /// <summary>Fetches a single item by its composite (hash + range) key, or <see langword="null"/> if not found.</summary>
        /// <param name="hashKey">The partition key value.</param>
        /// <param name="rangeKey">The sort key value.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<T?> GetById(DynamoDBValue hashKey, DynamoDBValue rangeKey, CancellationToken cancellationToken);

        /// <summary>Batch-fetches items by hash key. Missing keys are simply omitted from the result.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="hashKeys">The partition key values to fetch.</param>
        ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params DynamoDBValue[] hashKeys);

        /// <summary>Batch-fetches items by composite key. Missing keys are simply omitted from the result.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="keys">The (hash, range) key pairs to fetch.</param>
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
        /// <summary>Deletes the item identified by the given entity's key.</summary>
        ValueTask DeleteAsync(T item, CancellationToken cancellationToken);

        /// <summary>Deletes multiple items identified by the given entities' keys.</summary>
        ValueTask DeleteAsync(CancellationToken cancellationToken, params T[] items);

        /// <summary>Deletes the item with the given hash key.</summary>
        ValueTask DeleteAsync(DynamoDBValue id, CancellationToken cancellationToken);

        /// <summary>Deletes the item with the given composite (hash + sort) key.</summary>
        ValueTask DeleteAsync(DynamoDBValue hashKey, DynamoDBValue sortKey, CancellationToken cancellationToken);

        /// <summary>Begins a batched put/delete write (chunked into DynamoDB's 25-item batches automatically).</summary>
        IBatchWriteBuilder<T> BatchWrite();

        /// <summary>
        /// Begins a fluent key-condition query. Supports a hash key plus optional sort-key condition, secondary
        /// indexes (<see cref="IQueryBuilder{T}.UseIndex"/>), reverse ordering, consistent reads, and filters.
        /// </summary>
        IQueryBuilder<T> Query();

        /// <summary>
        /// Begins a fluent table/index scan with attribute filters. Scans read every item, so prefer
        /// <see cref="Query"/> when a key condition is available.
        /// </summary>
        IScanBuilder<T> Scan();

        /// <summary>
        /// Begins a partial, in-place update (patch) of the item with the given hash key — setting, incrementing,
        /// removing individual attributes, optionally guarded by conditional <c>If</c> checks — without reading
        /// or rewriting the whole item.
        /// </summary>
        /// <param name="hashKey">The partition key of the item to patch.</param>
        IPatchBuilder<T> Patch(DynamoDBValue hashKey);

        /// <summary>
        /// Begins a partial, in-place update (patch) of the item with the given composite (hash + range) key.
        /// </summary>
        /// <param name="hashKey">The partition key of the item to patch.</param>
        /// <param name="rangeKey">The sort key of the item to patch.</param>
        IPatchBuilder<T> Patch(DynamoDBValue hashKey, DynamoDBValue rangeKey);
    }
}
