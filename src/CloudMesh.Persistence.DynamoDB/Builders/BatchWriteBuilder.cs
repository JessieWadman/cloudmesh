using Amazon.DynamoDBv2.DataModel;

namespace CloudMesh.Persistence.DynamoDB.Builders
{
    public interface IBatchWriteBuilder<in T>
    {
        IBatchWriteBuilder<T> Save(params T[] items);
        IBatchWriteBuilder<T> Delete(params T[] items);
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
        private readonly Func<DynamoDBOperationConfig> config;
        private readonly HashSet<(T Item, BatchWriteOp Action)> items = new();

        public BatchWriteBuilder(DynamoDBContext context, Func<DynamoDBOperationConfig> config)
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
