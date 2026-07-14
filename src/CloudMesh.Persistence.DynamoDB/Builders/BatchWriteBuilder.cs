using Amazon.DynamoDBv2.DataModel;

namespace CloudMesh.Persistence.DynamoDB.Builders
{
    /// <summary>
    /// Fluent builder for a batched write. Queued puts and deletes are chunked into DynamoDB's 25-item batches
    /// automatically on <see cref="ExecuteAsync"/>. Batch writes are not transactional — use
    /// <see cref="ITransactWriteBuilder"/> when atomicity across items is required.
    /// </summary>
    public interface IBatchWriteBuilder<in T>
    {
        /// <summary>Queues one or more items to be put (created or overwritten).</summary>
        /// <param name="items">The items to save.</param>
        IBatchWriteBuilder<T> Save(params T[] items);

        /// <summary>Queues one or more items to be deleted (by their keys).</summary>
        /// <param name="items">The items to delete.</param>
        IBatchWriteBuilder<T> Delete(params T[] items);

        /// <summary>Executes all queued operations.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask ExecuteAsync(CancellationToken cancellationToken);
    }

    public class BatchWriteBuilder<T> : IBatchWriteBuilder<T>
    {
        public enum BatchWriteOp
        {
            Put,
            Delete
        }

        private readonly DynamoDBContext context;
        private readonly Func<BatchWriteConfig> config;
        private readonly HashSet<(T Item, BatchWriteOp Action)> items = new();

        public BatchWriteBuilder(DynamoDBContext context, Func<BatchWriteConfig> config)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            var batches = items.Chunk(25) // Max allowed batch size in DynamoDB
                .Select(batchedItems =>
                {
                    var batch = context.CreateBatchWrite<T>(config());
                    foreach (var record in batchedItems)
                        if (record.Action == BatchWriteOp.Delete)
                            batch.AddDeleteItem(record.Item);
                        else
                            batch.AddPutItem(record.Item);
                    return batch;
                })
                .ToArray();

            return new ValueTask(context.ExecuteBatchWriteAsync(batches, cancellationToken));
        }

        public IBatchWriteBuilder<T> Save(params T[] items)
        {
            foreach (var i in items)
                this.items.Add((i, BatchWriteOp.Put));
            return this;
        }

        public IBatchWriteBuilder<T> Delete(params T[] items)
        {
            foreach (var i in items)
                this.items.Add((i, BatchWriteOp.Delete));
            return this;
        }
    }
}
