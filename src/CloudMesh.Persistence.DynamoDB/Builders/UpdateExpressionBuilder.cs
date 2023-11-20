using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using CloudMesh.Persistence.DynamoDB.Helpers;
using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;

namespace CloudMesh.Persistence.DynamoDB.Builders
{
    public enum PatchCondition
    {
        Equals,
        NotEquals,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        StartsWith
    }

    public interface IUpdateExpressionBuilder { }

    public interface IUpdateExpressionBuilder<TEntity, TBuilder> : IUpdateExpressionBuilder
        where TBuilder : IUpdateExpressionBuilder
    {
        TBuilder If<R>(Expression<Func<TEntity, R>> property, PatchCondition condition, R value);
        TBuilder IfContains<R>(Expression<Func<TEntity, IEnumerable<R>>> property, R value);
        TBuilder IfSize<R>(Expression<Func<TEntity, R>> property, PatchCondition condition, int value);
        TBuilder Remove<R>(Expression<Func<TEntity, R>> property);
        TBuilder Set<R>(Expression<Func<TEntity, R>> property, R value);
        TBuilder Increment<R>(Expression<Func<TEntity, R>> property, R incrementBy);
        TBuilder Decrement<R>(Expression<Func<TEntity, R>> property, R incrementBy);
        TBuilder Add<R>(Expression<Func<TEntity, IEnumerable<R>>> property, params R[] elements);
        TBuilder With<R>(R value);
    }

    public abstract class UpdateExpressionBuilder<TEntity, TBuilder> : IUpdateExpressionBuilder, IUpdateExpressionBuilder<TEntity, TBuilder>
        where TBuilder : IUpdateExpressionBuilder
    {
        private int argCounter = 0;
        private readonly List<string> updateExpressions = new();
        private readonly Dictionary<string, AttributeValue> key;
        private readonly Dictionary<string, AttributeValue> expressionAttributeValues = new();
        private readonly Dictionary<string, string> expressionAttributeNames = new();
        private string conditionExpression = string.Empty;

        public UpdateExpressionBuilder(Dictionary<string, AttributeValue> key)
        {
            this.key = key;
        }

        private string GetArgName(string paramName) => $"A_{paramName.Replace(".", "_").Replace("[", "").Replace("]", "")}_{++argCounter}";
        private string GetAttrName(string attributeName) => $"#P_{attributeName}";

        public TBuilder Remove<R>(Expression<Func<TEntity, R>> property)
        {
            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var attrName = GetAttrName(propName);
            expressionAttributeNames[attrName] = propName;
            updateExpressions.Add($"REMOVE {attrName}");
            return (TBuilder)(IUpdateExpressionBuilder)this;
        }

        public TBuilder IfSize<R>(Expression<Func<TEntity, R>> property, PatchCondition condition, int value)
        {
            var expr = string.Empty;

            if (!string.IsNullOrWhiteSpace(conditionExpression))
                expr = conditionExpression + " AND ";

            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = ":" + GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);
            var attrName = GetAttrName(propName);
            expressionAttributeNames[attrName] = propName;

            expressionAttributeValues[$"{argName}"] = AttributeHelper.ToAttributeValue(value, propertyInfo);

            expr += $"({OpToStr()})";

            conditionExpression = expr;

            string OpToStr() => condition switch
            {
                PatchCondition.Equals => $"size({attrName}) = {argName}",
                PatchCondition.NotEquals => $"size({attrName}) <> {argName}",
                PatchCondition.LessThan => $"size({attrName}) < {argName}",
                PatchCondition.LessThanOrEqual => $"size({attrName}) <= {argName}",
                PatchCondition.GreaterThan => $"size({attrName}) > {argName}",
                PatchCondition.GreaterThanOrEqual => $"size({attrName}) >= {argName}",
                _ => throw new InvalidOperationException()
            };

            return (TBuilder)(IUpdateExpressionBuilder)this;
        }
        public TBuilder If<R>(Expression<Func<TEntity, R>> property, PatchCondition condition, R value)
        {
            var expr = string.Empty;

            if (!string.IsNullOrWhiteSpace(conditionExpression))
                expr = conditionExpression + " AND ";

            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = ":" + GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);
            var attrName = GetAttrName(propName);
            expressionAttributeNames[attrName] = propName;

            expressionAttributeValues[$"{argName}"] = AttributeHelper.ToAttributeValue(value, propertyInfo);

            expr += $"({OpToStr()})";

            conditionExpression = expr;

            string OpToStr() => condition switch
            {
                PatchCondition.Equals => $"{attrName} = {argName}",
                PatchCondition.NotEquals => $"{attrName} <> {argName}",
                PatchCondition.LessThan => $"{attrName} < {argName}",
                PatchCondition.GreaterThan => $"{attrName} > {argName}",
                PatchCondition.LessThanOrEqual => $"{attrName} <= {argName}",
                PatchCondition.GreaterThanOrEqual => $"{attrName} >= {argName}",
                PatchCondition.StartsWith => $"begins_with({attrName}, {argName})",
                _ => throw new InvalidOperationException()
            };

            return (TBuilder)(IUpdateExpressionBuilder)this;
        }

        public TBuilder IfContains<R>(Expression<Func<TEntity, IEnumerable<R>>> property, R value)
        {
            var expr = string.Empty;

            if (!string.IsNullOrWhiteSpace(conditionExpression))
                expr = conditionExpression + " AND ";

            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);
            var attrName = GetAttrName(propName);
            expressionAttributeNames[attrName] = propName;

            expressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(value, propertyInfo);

            expr += $"(contains({attrName}, {argName}))";

            conditionExpression = expr;

            return (TBuilder)(IUpdateExpressionBuilder)this;
        }

        public TBuilder Increment<R>(Expression<Func<TEntity, R>> property, R incrementBy)
        {
            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);
            var attrName = GetAttrName(propName);
            expressionAttributeNames[attrName] = propName;

            updateExpressions.Add($"SET {attrName} = {attrName} + :{argName}");
            expressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(incrementBy, propertyInfo);
            return (TBuilder)(IUpdateExpressionBuilder)this;
        }

        public TBuilder Decrement<R>(Expression<Func<TEntity, R>> property, R incrementBy)
        {
            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);
            var attrName = GetAttrName(propName);
            expressionAttributeNames[attrName] = propName;

            updateExpressions.Add($"SET {attrName} = {attrName} - :{argName}");
            expressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(incrementBy, propertyInfo);
            return (TBuilder)(IUpdateExpressionBuilder)this;
        }

        public TBuilder Add<R>(Expression<Func<TEntity, IEnumerable<R>>> property, params R[] elements)
        {
            var propName = ExpressionHelper.GetDynamoDBPropertyName(property);
            var argName = GetArgName(propName);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);

            updateExpressions.Add($"SET #{argName} = list_append(if_not_exists(#{argName}, :empty_list), :{argName})");
            expressionAttributeNames[$"#{argName}"] = propName;
            var values = elements.Select(e => Convert.ToString(e, CultureInfo.InvariantCulture)).ToList();
            expressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(elements, propertyInfo);

            // Adding empty list if list does not exist on item yet. Otherwise will throw exception
            expressionAttributeValues[$":empty_list"] = new AttributeValue() { IsLSet = true };
            return (TBuilder)(IUpdateExpressionBuilder)this; 
        }

        public TBuilder Set<R>(Expression<Func<TEntity, R>> property, R value)
        {
            var (propertyPath, _) = ExpressionHelper.GetDotNotation(property);
            var argName = GetArgName(propertyPath);
            var propertyInfo = ExpressionHelper.GetPropertyInfo(property);

            var exprPath = ExpressionHelper.DotNotationToDynamoDBExpression<TEntity>(propertyPath);
            if (value is null || 
                value is string str && string.IsNullOrWhiteSpace(str) ||
                value is ICollection coll && coll.Count == 0
            )
                updateExpressions.Add($"REMOVE {exprPath}");
            else
            {
                expressionAttributeValues[$":{argName}"] = AttributeHelper.ToAttributeValue(value, propertyInfo);
                updateExpressions.Add($"SET {exprPath} = :{argName}");
            }
            return (TBuilder)(IUpdateExpressionBuilder)this;
        }

        public TBuilder With<R>(R entityRelacementValues)
        {
            var replacementValues = Document.FromJson(JsonSerializer.Serialize(entityRelacementValues)).ToAttributeUpdateMap(DynamoDBEntryConversion.V2, false);
            foreach (var attributeUpdate in replacementValues)
            {
                var propName = attributeUpdate.Key;
                var argName = GetArgName(attributeUpdate.Key);
                var attrName = GetAttrName(propName);
                expressionAttributeNames[attrName] = propName;


                expressionAttributeValues[$":{argName}"] = attributeUpdate.Value.Value;
                updateExpressions.Add($"SET {attrName} = :{argName}");
            }
            return (TBuilder)(IUpdateExpressionBuilder)this;
        }

        public (
            string? UpdateExpression,
            string ConditionExpression,
            Dictionary<string, AttributeValue> Key,
            Dictionary<string, string> ExpressionAttributeNames,
            Dictionary<string, AttributeValue> ExpressionAttributeValues) Build(bool returnValues)
        {
            if (updateExpressions.Count == 0)
                return (null, conditionExpression, key, expressionAttributeNames, expressionAttributeValues);

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

            var updateExpression = string.Join("  ", groupedExpressions);

            return (updateExpression, conditionExpression, key, expressionAttributeNames, expressionAttributeValues);
        }
    }
}
