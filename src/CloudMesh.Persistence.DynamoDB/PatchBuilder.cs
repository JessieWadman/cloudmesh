using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using CloudMesh.Persistence.DynamoDB.Helpers;
using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;

namespace CloudMesh.Persistence.DynamoDB
{
    public class PatchBuilder<T> : IPatchBuilder<T>
    {
        private readonly UpdateItemRequest request;
        private int argCounter = 0;
        private readonly IAmazonDynamoDB client;
        private readonly List<string> updateExpressions = new();

        public PatchBuilder(IAmazonDynamoDB client, string tableName, Dictionary<string, AttributeValue> key)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            request = new UpdateItemRequest
            {
                TableName = tableName,
                Key = key
            };
        }

        private string GetArgName(string paramName) => $"A_{paramName.Replace(".", "_").Replace("[", "").Replace("]", "")}_{++argCounter}";

        public IPatchBuilder<T> Remove<R>(Expression<Func<T, R>> property)
        {
            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            updateExpressions.Add($"REMOVE {propName}");            
            return this;
        }


        public IPatchBuilder<T> IfSize<R>(Expression<Func<T, R>> property, PatchCondition condition, int value)
        {
            var expr = string.Empty;

            if (!string.IsNullOrWhiteSpace(request.ConditionExpression))
                expr = request.ConditionExpression + " AND ";

            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = ":" + GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);

            request.ExpressionAttributeValues[$"{argName}"] = AttributeHelper.ToAttributeValue(value, propertyInfo);

            expr += $"({OpToStr()})";

            request.ConditionExpression = expr;

            string OpToStr() => condition switch
            {
                PatchCondition.Equals => $"size({propName}) = {argName}",
                PatchCondition.NotEquals => $"size({propName}) <> {argName}",
                PatchCondition.LessThan => $"size({propName}) < {argName}",
                PatchCondition.LessThanOrEqual => $"size({propName}) <= {argName}",
                PatchCondition.GreaterThan => $"size({propName}) > {argName}",
                PatchCondition.GreaterThanOrEqual => $"size({propName}) >= {argName}",
                _ => throw new InvalidOperationException()
            };

            return this;
        }
        public IPatchBuilder<T> If<R>(Expression<Func<T, R>> property, PatchCondition condition, R value)
        {
            var expr = string.Empty;

            if (!string.IsNullOrWhiteSpace(request.ConditionExpression))
                expr = request.ConditionExpression + " AND ";

            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = ":" + GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);

            request.ExpressionAttributeValues[$"{argName}"] = AttributeHelper.ToAttributeValue(value, propertyInfo);
            
            expr += $"({OpToStr()})";

            request.ConditionExpression = expr;

            string OpToStr() => condition switch
            {
                PatchCondition.Equals => $"{propName} = {argName}",
                PatchCondition.NotEquals => $"{propName} <> {argName}",
                PatchCondition.LessThan => $"{propName} < {argName}",
                PatchCondition.GreaterThan => $"{propName} > {argName}",
                PatchCondition.LessThanOrEqual => $"{propName} <= {argName}",
                PatchCondition.GreaterThanOrEqual => $"{propName} >= {argName}",
                PatchCondition.StartsWith => $"begins_with({propName}, {argName})",
                _ => throw new InvalidOperationException()
            };

            return this;
        }

        public IPatchBuilder<T> IfContains<R>(Expression<Func<T, IEnumerable<R>>> property, R value)
        {
            var expr = string.Empty;

            if (!string.IsNullOrWhiteSpace(request.ConditionExpression))
                expr = request.ConditionExpression + " AND ";

            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);

            request.ExpressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(value, propertyInfo);

            expr += $"(contains({propName}, {argName}))";

            request.ConditionExpression = expr;

            return this;
        }        

        public IPatchBuilder<T> Increment<R>(Expression<Func<T, R>> property, R incrementBy)
        {
            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);

            updateExpressions.Add($"SET {propName} = {propName} + :{argName}");
            request.ExpressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(incrementBy, propertyInfo);
            return this;
        }

        public IPatchBuilder<T> Decrement<R>(Expression<Func<T, R>> property, R incrementBy)
        {
            var propName = ExpressionHelper.GetDynamoDBPropertyName(property); 
            var argName = GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);

            updateExpressions.Add($"SET {propName} = {propName} - :{argName}");
            request.ExpressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(incrementBy, propertyInfo);
            return this;
        }

        public IPatchBuilder<T> Add<R>(Expression<Func<T, IEnumerable<R>>> property, params R[] elements)
        {
            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);

            updateExpressions.Add($"SET #{argName} = list_append(if_not_exists(#{argName}, :empty_list), :{argName})");
            request.ExpressionAttributeNames[$"#{argName}"] = propName;
            var values = elements.Select(e => Convert.ToString(e, CultureInfo.InvariantCulture)).ToList();
            request.ExpressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(elements, propertyInfo);

            // Adding empty list if list does not exist on item yet. Otherwise will throw exception
            request.ExpressionAttributeValues[$":empty_list"] = new AttributeValue() { IsLSet = true };
            return this;
        }

        public IPatchBuilder<T> Set<R>(Expression<Func<T, R>> property, R value)
        {
            var (propertyPath, _) = ExpressionHelper.GetDotNotation(property);
            var argName = GetArgName(propertyPath);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);

            var exprPath = ExpressionHelper.DotNotationToDynamoDBExpression<T>(propertyPath);
            if (value is null || (
                (value is string str && string.IsNullOrWhiteSpace(str)) || 
                (value is ICollection coll && coll.Count == 0))
            )
                updateExpressions.Add($"REMOVE {exprPath}");
            else
            {
                request.ExpressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(value, propertyInfo);
                updateExpressions.Add($"SET {exprPath} = :{argName}");
            }
            return this;
        }

        public IPatchBuilder<T> With<R>(R value)
        {
            foreach (var attrib in Document.FromJson(JsonSerializer.Serialize(value)).ToAttributeUpdateMap(DynamoDBEntryConversion.V2, false))
            {
                var propName = attrib.Key;
                var argName = GetArgName(attrib.Key);
                request.ExpressionAttributeValues[$":{argName}"] = attrib.Value.Value;
                updateExpressions.Add($"SET {propName} = :{argName}");                
            }
            return this;
        }

        public async ValueTask<UpdateItemResponse> InternalExecuteAsync(bool returnValues, CancellationToken cancellationToken)
        {
            if (updateExpressions.Count == 0)
                return null;

            // Splits "SET A = B" into "SET", "A = B"
            var allExpressionsWithOperations = from e in updateExpressions
                                               let expressionParts = e.Split(' ')
                                               let op = expressionParts.First()
                                               let args = string.Join(" ", expressionParts.Skip(1))
                                               select new { op, args };

            // Aggregates all ("SET, "A=B"), ("SET", "B=C") into "SET", "A=B, B=C"
            var groupedExpressions = from e in allExpressionsWithOperations
                                     group e by e.op into g
                                     select g.Key + " " + string.Join(", ", g.Select(x => x.args));

            request.UpdateExpression = string.Join("  ", groupedExpressions);

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

        public async ValueTask<T> ExecuteAndGetAsync(CancellationToken cancellationToken)
        {
            var response = await InternalExecuteAsync(true, cancellationToken);
            if (response is null)
                return default;

            if ((int)response.HttpStatusCode < 200 || (int)response.HttpStatusCode >= 400)
                throw new InvalidOperationException($"Failed to patch Item: {response.HttpStatusCode}");

            var doc = Document.FromAttributeMap(response.Attributes);
            using var ctx = new DynamoDBContext(client);
            return ctx.FromDocument<T>(doc);
        }
    }
}
