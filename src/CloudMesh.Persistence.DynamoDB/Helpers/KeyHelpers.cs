using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using System.Collections.Concurrent;
using System.Reflection;

namespace CloudMesh.Persistence.DynamoDB.Helpers
{
    internal static class KeyHelpers
    {
        private static readonly ConcurrentDictionary<Type, Func<object, Dictionary<string, AttributeValue>>> mapKeyCache = new();

        private static Func<object, Dictionary<string, AttributeValue>> GetKeyMapLambda<T>(DynamoDBContext context)
        {
            return mapKeyCache.GetOrAdd(typeof(T), t => CreateKeyMapLambda<T>(context));
        }

        private static Func<object, Dictionary<string, AttributeValue>> CreateKeyMapLambda<T>(DynamoDBContext context)
        {
            var keyProp = ExpressionHelper.TryGetHashKeyProperty<T>();
            var rangeProp = ExpressionHelper.TryGetRangeKeyProperty<T>();
            var keyPropName = keyProp != null ? ExpressionHelper.GetDynamoDBPropertyName(keyProp) : null;
            var rangePropName = rangeProp != null ? ExpressionHelper.GetDynamoDBPropertyName(rangeProp) : null;

            if (keyProp != null && rangeProp != null)
            {
                return obj => MapHashAndRangeKey(obj, keyProp, keyPropName!, rangeProp, rangePropName!);
            }
            else if (keyProp != null)
            {
                return obj => MapHashKey(obj, keyProp, keyPropName!);
            }
            else
            {
                return obj => context.ToDocument((T)obj).ToAttributeMap();
            }
        }

        public static Dictionary<string, AttributeValue> MapKey<T>(DynamoDBContext context, T item) => GetKeyMapLambda<T>(context)(item!);

        public static Dictionary<string, AttributeValue> MapHashAndRangeKey(object item,
            PropertyInfo hashKeyProp, string hashKeyAttributeName,
            PropertyInfo rangeKeyProp, string rangeKeyAttributeName)
        {
            return new()
            {
                [hashKeyAttributeName] = AttributeHelper.ToAttributeValue(hashKeyProp.GetValue(item), hashKeyProp),
                [rangeKeyAttributeName] = AttributeHelper.ToAttributeValue(rangeKeyProp.GetValue(item), rangeKeyProp)
            };
        }

        public static Dictionary<string, AttributeValue> MapHashKey(object item,
            PropertyInfo hashKeyProp, string hashKeyAttributeName)
        {
            return new()
            {
                [hashKeyAttributeName] = AttributeHelper.ToAttributeValue(hashKeyProp.GetValue(item), hashKeyProp)
            };
        }
    }
}
