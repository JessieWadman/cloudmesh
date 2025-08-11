using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using CloudMesh.Persistence.DynamoDB.Builders;
using CloudMesh.Persistence.DynamoDB.Helpers;

namespace CloudMesh.Persistence.DynamoDB
{
    public class DynamoDBRepository<T> : IRepository<T>
    {
        private bool disposed;
        private readonly string tableName;
        private readonly IAmazonDynamoDB dynamoDB;
        private readonly DynamoDBContext context;

        public DynamoDBRepository(string tableName, IAmazonDynamoDB dynamoDB)
        {
            this.tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
            this.dynamoDB = dynamoDB ?? throw new ArgumentNullException(nameof(dynamoDB));
            
            this.context = new DynamoDBContextBuilder()
                .WithDynamoDBClient(() => dynamoDB)
                .ConfigureContext(opts =>
                {
                    opts.Conversion = DynamoDBEntryConversion.V2;
                })
                .Build();
        }

        ~DynamoDBRepository()
        {
            Dispose(false);
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

        protected virtual TConfig GetOperationConfig<TConfig>()
            where TConfig : BaseOperationConfig, new()
        {
            var config = new TConfig
            {
                OverrideTableName = this.tableName,
                Conversion = DynamoDBEntryConversion.V2
            };
            return config;
        }
        
        protected virtual DynamoDBOperationConfig GetOperationConfigV1(string? indexName = null)
        {
            var config = new DynamoDBOperationConfig
            {
                OverrideTableName = this.tableName,
                Conversion = DynamoDBEntryConversion.V2,
                IndexName = indexName
            };
            return config;
        }
        
        public ValueTask DeleteAsync(DynamoDBValue hashKey, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return new ValueTask(context.DeleteAsync<T>(hashKey.Value, GetOperationConfig<DeleteConfig>(), cancellationToken));
        }

        public ValueTask DeleteAsync(T item, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return new ValueTask(context.DeleteAsync(item, GetOperationConfig<DeleteConfig>(), cancellationToken));
        }

        public ValueTask DeleteAsync(DynamoDBValue hashKey, DynamoDBValue rangeKey, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return new ValueTask(context.DeleteAsync<T>(
                hashKey: hashKey.Value, 
                rangeKey: rangeKey.Value, 
                deleteConfig: GetOperationConfig<DeleteConfig>(), 
                cancellationToken: cancellationToken));
        }

        public async ValueTask<T?> GetById(DynamoDBValue hashKey, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await context.LoadAsync<T>(hashKey.Value, GetOperationConfig<LoadConfig>(), cancellationToken);
        }

        public async ValueTask<T?> GetById(DynamoDBValue hashKey, DynamoDBValue rangeKey, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await context.LoadAsync<T>(hashKey.Value, rangeKey.Value, GetOperationConfig<LoadConfig>(), cancellationToken);
        }
        
        public async ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params DynamoDBValue[] hashKeys)
        {
            ThrowIfDisposed();

            var hashKeyName = ExpressionHelper.GetHashKeyProperty<T>().Name;

            var opConfig = GetOperationConfig<LoadConfig>();
            var result = new List<T>();
            foreach (var batch in hashKeys.Chunk(25))
            {
                var getResult = await dynamoDB.BatchGetItemAsync(new BatchGetItemRequest()
                {
                    RequestItems = new()
                    {
                        [opConfig.OverrideTableName] = new()
                        {
                            Keys = batch
                                .Select(id => new Dictionary<string, AttributeValue>() {
                                    [hashKeyName] = new()
                                    {
                                        S = id.ToObject()?.ToString()
                                    }
                                })
                                .ToList()
                        }
                    }
                }, cancellationToken);

                var rows = getResult.Responses.Values.First().Select(map =>
                    context.FromDocument<T>(Document.FromAttributeMap(map)));
                result.AddRange(rows);
                /*
                var batchGet = context.CreateBatchGet<T>(GetOperationConfig());
                foreach (var key in batch)
                    batchGet.AddKey(key.ToObject());
                await context.ExecuteBatchGetAsync(new BatchGet[] { batchGet }, cancellationToken);
                result.AddRange(batchGet.Results);*/
            }

            return result.ToArray();
        }

        public async ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys)
        {
            ThrowIfDisposed();
            var result = new List<T>();

            foreach (var keyBatch in keys.Chunk(25))
            {
                var batch = context.CreateBatchGet<T>(GetOperationConfig<BatchGetConfig>());
                foreach (var (hashKey, rangeKey) in keyBatch)
                    batch.AddKey(hashKey.Value, rangeKey.Value);
                await context.ExecuteBatchGetAsync([batch], cancellationToken);
                result.AddRange(batch.Results);
            }

            return result.ToArray();
        }

        public IQueryBuilder<T> Query()
        {
            ThrowIfDisposed();
            return new QueryBuilder<T>(context, GetOperationConfig<QueryConfig>);
        }

        public ValueTask SaveAsync(T item, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return new ValueTask(context.SaveAsync(item, GetOperationConfig<SaveConfig>(), cancellationToken));            
        }
        
        public async ValueTask<bool> CreateAsync(T item, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            
            var attributes = context.ToDocument(item).ToAttributeMap();

            var hashKeyProperty = ExpressionHelper.TryGetHashKeyProperty<T>();
            if (hashKeyProperty == null)
                throw new InvalidOperationException($"{typeof(T).Name} does not have a hash key!");
            
            var hashAttributeName = ExpressionHelper.GetDynamoDBPropertyName(hashKeyProperty);
            
            var putRequest = new PutItemRequest
            {
                Item = attributes,
                ConditionExpression = $"attribute_not_exists({hashAttributeName})",
                TableName = this.tableName
            };

            try
            {
                var result = await dynamoDB.PutItemAsync(putRequest, cancellationToken);
                if ((int)result.HttpStatusCode < 200 || (int)result.HttpStatusCode >= 300)
                    throw new HttpRequestException("The request was not successful", null, result.HttpStatusCode);
                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
        }

        public ValueTask SaveAsync(IEnumerable<T> items, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return BatchWrite()
                .Save(items.ToArray())
                .ExecuteAsync(cancellationToken);
        }        

        public IScanBuilder<T> Scan()
        {
            ThrowIfDisposed();
            return new ScanBuilder<T>(context, this.GetOperationConfig<ScanConfig>);
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken, params T[] items)
        {
            ThrowIfDisposed();
            return BatchWrite()
                .Delete(items)
                .ExecuteAsync(cancellationToken);
        }

        public IBatchWriteBuilder<T> BatchWrite()
        {
            ThrowIfDisposed();
            return new BatchWriteBuilder<T>(context, GetOperationConfig<BatchWriteConfig>);
        }

        public IPatchBuilder<T> Patch(DynamoDBValue hashKey)
        {
            ThrowIfDisposed();
            var key = new Dictionary<string, AttributeValue>();

            var hashKeyProperty = ExpressionHelper.GetDynamoDBPropertyName(
                ExpressionHelper.GetHashKeyProperty<T>());
            key[hashKeyProperty] = hashKey.ToAttributeValue();

            return new PatchBuilder<T>(this.dynamoDB, GetOperationConfigV1().OverrideTableName, key);
        }

        public IPatchBuilder<T> Patch(DynamoDBValue hashKey, DynamoDBValue rangeKey)
        {
            ThrowIfDisposed();
            var key = new Dictionary<string, AttributeValue>();
            
            var hashKeyProperty = ExpressionHelper.GetDynamoDBPropertyName(
                ExpressionHelper.GetHashKeyProperty<T>());
            key[hashKeyProperty] = hashKey.ToAttributeValue();

            var rangeKeyProperty = ExpressionHelper.GetDynamoDBPropertyName(
                ExpressionHelper.GetRangeKeyProperty<T>());
            key[rangeKeyProperty] = rangeKey.ToAttributeValue();

            return new PatchBuilder<T>(this.dynamoDB, GetOperationConfigV1().OverrideTableName, key);
        }
    }
}
