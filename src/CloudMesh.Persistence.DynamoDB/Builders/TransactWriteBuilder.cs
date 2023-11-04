using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using CloudMesh.Persistence.DynamoDB.Helpers;

namespace CloudMesh.Persistence.DynamoDB.Builders
{
    public interface ITransactWritePatchBuilder<T> : IUpdateExpressionBuilder<T, ITransactWritePatchBuilder<T>>
    {
        ITransactWriteBuilder Build();
    }

    public interface ITransactWriteBuilder
    {
        ITransactWriteBuilder Save<T>(string tableName, params T[] items);
        ITransactWritePatchBuilder<T> Patch<T>(string tableName, T recordKey);
        ITransactWritePatchBuilder<T> Patch<T>(string tableName, DynamoDBValue hashKey);
        ITransactWritePatchBuilder<T> Patch<T>(string tableName, DynamoDBValue hashKey, DynamoDBValue rangeKey);
        ITransactWriteBuilder Delete<T>(string tableName, params DynamoDBValue[] hashKeys);
        ITransactWriteBuilder Delete<T>(string tableName, params T[] items);
        ITransactWriteBuilder Delete<T>(string tableName, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys);
        /// <summary>
        /// Executes the transactions
        /// </summary>
        /// <param name="cancellationToken">A cancellation token</param>
        /// <returns>True if the transaction was successfully written, false if a condition specified via the "If" statement was not met</returns>
        /// <exception cref="InvalidOperationException">Thrown in case the operation failed due to insufficient permissions, tables does not exist of otherwise</exception>
        ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken);
    }

    public class TransactWritePatchBuilder<T> : UpdateExpressionBuilder<T, ITransactWritePatchBuilder<T>>, ITransactWritePatchBuilder<T>
    {
        private readonly TransactWriteBuilder parent;
        private readonly string tableName;

        public TransactWritePatchBuilder(TransactWriteBuilder parent, string tableName, Dictionary<string, AttributeValue> key)
            : base(key)
        {
            this.parent = parent;
            this.tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        }

        public ITransactWriteBuilder Build()
        {
            var (updateExpression, conditionExpression, key, expressionAttributeNames, expressionAttributeValues) = Build(false);
            if (updateExpression != null)
            {
                parent.AddPatch(new()
                {
                    Update = new()
                    {
                        TableName = tableName,
                        Key = key,
                        ConditionExpression = conditionExpression,
                        UpdateExpression = updateExpression,
                        ExpressionAttributeNames = expressionAttributeNames,
                        ExpressionAttributeValues = expressionAttributeValues,
                        ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.ALL_OLD
                    }
                });
            }

            return parent;
        }
    }

    public class TransactWriteBuilder : ITransactWriteBuilder, IDisposable
    {
        private readonly TransactWriteItemsRequest request = new() { TransactItems = new() };
        private readonly IAmazonDynamoDB client;
        private readonly DynamoDBContext context;
        private bool disposed;

        public TransactWriteBuilder(IAmazonDynamoDB client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(TransactWriteBuilder.client));
            context = new DynamoDBContext(client, new DynamoDBContextConfig
            {
                Conversion = DynamoDBEntryConversion.V2
            });
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        protected virtual void Dispose(bool disposeManagedResources)
        {
            ThrowIfDisposed();

            if (disposeManagedResources)
                context.Dispose();
            disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual DynamoDBOperationConfig GetOperationConfig(string tableName)
        {
            var config = new DynamoDBOperationConfig
            {
                OverrideTableName = tableName,
                Conversion = DynamoDBEntryConversion.V2
            };
            return config;
        }

        public async ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await client.TransactWriteItemsAsync(request, cancellationToken);
                if ((int)response.HttpStatusCode < 200 || (int)response.HttpStatusCode >= 400)
                {
                    throw new InvalidOperationException($"Failed to execute transaction: {response.HttpStatusCode}");
                }
                return true;
            }
            catch (TransactionCanceledException ce)
            {
                if (ce.CancellationReasons.Any(e => e.Code == "ConditionalCheckFailed"))
                    return false;
                throw;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
        }

        public ITransactWriteBuilder Save<T>(string tableName, params T[] items)
        {
            ThrowIfDisposed();

            foreach (var item in items)
            {
                request.TransactItems.Add(new()
                {
                    Put = new()
                    {
                        TableName = tableName,
                        Item = context.ToDocument(item).ToAttributeMap()
                    }
                });
            }

            return this;
        }

        public ITransactWritePatchBuilder<T> Patch<T>(string tableName, T recordKey)
        {
            ThrowIfDisposed();

            var key = KeyHelpers.MapKey(context, recordKey);
            return new TransactWritePatchBuilder<T>(this, tableName, key);
        }

        public ITransactWritePatchBuilder<T> Patch<T>(string tableName, DynamoDBValue hashKey)
        {
            ThrowIfDisposed();

            var key = new Dictionary<string, AttributeValue>()
            {
                [ExpressionHelper.GetDynamoDBPropertyName(ExpressionHelper.GetHashKeyProperty<T>())] = hashKey.ToAttributeValue()
            };

            return new TransactWritePatchBuilder<T>(this, tableName, key);
        }

        public ITransactWritePatchBuilder<T> Patch<T>(string tableName, DynamoDBValue hashKey, DynamoDBValue rangeKey)
        {
            ThrowIfDisposed();

            var key = new Dictionary<string, AttributeValue>()
            {
                [ExpressionHelper.GetDynamoDBPropertyName(ExpressionHelper.GetHashKeyProperty<T>())] = hashKey.ToAttributeValue(),
                [ExpressionHelper.GetDynamoDBPropertyName(ExpressionHelper.GetRangeKeyProperty<T>())] = rangeKey.ToAttributeValue()
            };

            return new TransactWritePatchBuilder<T>(this, tableName, key);
        }

        internal void AddPatch(TransactWriteItem patch)
        {
            ThrowIfDisposed();
            request.TransactItems.Add(patch);
        }

        public ITransactWriteBuilder Delete<T>(string tableName, params DynamoDBValue[] hashKeys)
        {
            ThrowIfDisposed();

            foreach (var item in hashKeys)
            {
                var key = new Dictionary<string, AttributeValue>()
                {
                    [ExpressionHelper.GetDynamoDBPropertyName(ExpressionHelper.GetHashKeyProperty<T>())] = item.ToAttributeValue()
                };

                request.TransactItems.Add(new()
                {
                    Delete = new()
                    {
                        TableName = tableName,
                        Key = key
                    }
                });
            }

            return this;
        }

        public ITransactWriteBuilder Delete<T>(string tableName, params T[] items)
        {
            ThrowIfDisposed();
            using var dbContext = new DynamoDBContext(client);

            foreach (var item in items)
            {
                request.TransactItems.Add(new()
                {
                    Delete = new()
                    {
                        TableName = tableName,
                        Key = KeyHelpers.MapKey(context, item)
                    }
                });
            }

            return this;
        }

        public ITransactWriteBuilder Delete<T>(string tableName, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys)
        {
            ThrowIfDisposed();
            using var dbContext = new DynamoDBContext(client);

            foreach (var (hashKey, rangeKey) in keys)
            {
                var key = new Dictionary<string, AttributeValue>()
                {
                    [ExpressionHelper.GetDynamoDBPropertyName(ExpressionHelper.GetHashKeyProperty<T>())] = hashKey.ToAttributeValue(),
                    [ExpressionHelper.GetDynamoDBPropertyName(ExpressionHelper.GetRangeKeyProperty<T>())] = rangeKey.ToAttributeValue()
                };

                request.TransactItems.Add(new()
                {
                    Delete = new()
                    {
                        TableName = tableName,
                        Key = key
                    }
                });
            }

            return this;
        }
    }
}
