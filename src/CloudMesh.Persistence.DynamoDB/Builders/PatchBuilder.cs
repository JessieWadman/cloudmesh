using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;

namespace CloudMesh.Persistence.DynamoDB.Builders
{
    /// <summary>
    /// Fluent builder for a partial, in-place update of a single item. Inherits the mutation/condition verbs from
    /// <see cref="IUpdateExpressionBuilder{TEntity,TBuilder}"/> (<c>Set</c>, <c>Remove</c>, <c>Increment</c>,
    /// <c>Add</c>, <c>If</c>, ...) and executes them as one DynamoDB <c>UpdateItem</c>. Conditional (<c>If</c>)
    /// failures do not throw — they surface as <see langword="false"/> / <see langword="null"/>.
    /// </summary>
    public interface IPatchBuilder<T> : IUpdateExpressionBuilder<T, IPatchBuilder<T>>
    {
        /// <summary>
        /// Applies the update. Returns <see langword="true"/> on success, or <see langword="false"/> if a
        /// conditional <c>If</c> check failed (the item was left unchanged).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Applies the update and returns the new item state, or <see langword="null"/> if a conditional <c>If</c>
        /// check failed (the item was left unchanged).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<T?> ExecuteAndGetAsync(CancellationToken cancellationToken);
    }

    public class PatchBuilder<T> : UpdateExpressionBuilder<T, IPatchBuilder<T>>, IPatchBuilder<T>
    {
        private readonly IAmazonDynamoDB client;
        private readonly string tableName;
        private readonly Dictionary<string, AttributeValue> key;

        public PatchBuilder(IAmazonDynamoDB client, string tableName, Dictionary<string, AttributeValue> key)
            : base(key)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
            this.key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public async ValueTask<UpdateItemResponse?> InternalExecuteAsync(bool returnValues, CancellationToken cancellationToken)
        {
            var patch = Build(returnValues);

            if (patch.UpdateExpression == null)
                return null;

            var request = new UpdateItemRequest
            {
                TableName = tableName,
                Key = key,
                ConditionExpression = patch.ConditionExpression,
                UpdateExpression = patch.UpdateExpression,
                ExpressionAttributeNames = patch.ExpressionAttributeNames,
                ExpressionAttributeValues = patch.ExpressionAttributeValues
            };

            request.ReturnValues = returnValues
                ? ReturnValue.ALL_NEW
                : ReturnValue.NONE;

            try
            {
                var response = await client.UpdateItemAsync(request, cancellationToken);
                if ((int)response.HttpStatusCode < 200 || (int)response.HttpStatusCode >= 400)
                    throw new InvalidOperationException($"Failed to patch Item: {response.HttpStatusCode}");
                return response;
            }
            catch (ConditionalCheckFailedException)
            {
                return null;
            }
        }

        public async ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken)
        {
            var response = await InternalExecuteAsync(false, cancellationToken);
            if (response is null)
                return false;

            if ((int)response.HttpStatusCode < 200 || (int)response.HttpStatusCode >= 400)
                throw new InvalidOperationException($"Failed to patch Item: {response.HttpStatusCode}");
            return true;
        }

        public async ValueTask<T?> ExecuteAndGetAsync(CancellationToken cancellationToken)
        {
            var response = await InternalExecuteAsync(true, cancellationToken);
            if (response is null)
                return default;

            if ((int)response.HttpStatusCode < 200 || (int)response.HttpStatusCode >= 400)
                throw new InvalidOperationException($"Failed to patch Item: {response.HttpStatusCode}");

            var doc = Document.FromAttributeMap(response.Attributes);
            using var ctx = new DynamoDBContextBuilder()
                .WithDynamoDBClient(() => client)
                .Build();
            
            return ctx.FromDocument<T>(doc);
        }
    }
}
