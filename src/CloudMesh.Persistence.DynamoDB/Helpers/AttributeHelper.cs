using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using CloudMesh.Persistence.DynamoDB.Converters;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CloudMesh.Persistence.DynamoDB
{
    public static class AttributeHelper
    {
        public static AttributeValue ToAttributeValue<R>(R value, PropertyInfo propertyInfo)
        {
            if (typeof(R).IsEnum)
            {
                var customAttribute = Attribute.GetCustomAttribute(propertyInfo, typeof(DynamoDBPropertyAttribute), true) as DynamoDBPropertyAttribute;
                if (customAttribute?.Converter != null && customAttribute.Converter == typeof(DynamoDBEnumToStringConverter<R>))
                {
                    return new AttributeValue { S = value.ToString() };
                }

                return new AttributeValue { N = ((int)(object)value!).ToString() };
            }

            if (value is null && typeof(R) == typeof(string))
                return new AttributeValue { S = null };

            return value switch
            {
                null => new AttributeValue { NULL = true },
                Guid guid => new AttributeValue { S = guid.ToString() },
                byte[] byteArr => new AttributeValue { B = new MemoryStream(byteArr) },
                bool b => new AttributeValue { BOOL = b },
                int i => new AttributeValue { N = i.ToString() },
                byte b => new AttributeValue { N = b.ToString() },
                short i => new AttributeValue { N = i.ToString() },
                uint i => new AttributeValue { N = i.ToString() },
                ushort i => new AttributeValue { N = i.ToString() },
                long i => new AttributeValue { N = i.ToString() },
                ulong i => new AttributeValue { N = i.ToString() },
                decimal i => new AttributeValue { N = i.ToString(CultureInfo.InvariantCulture) },
                double i => new AttributeValue { N = i.ToString(CultureInfo.InvariantCulture) },
                float i => new AttributeValue { N = i.ToString(CultureInfo.InvariantCulture) },
                DateTimeOffset i => new AttributeValue { S = i.ToISO8601() },
                DateTime i => new AttributeValue { S = new DateTimeOffset(i.ToUniversalTime(), TimeSpan.Zero).ToISO8601() },
                DateOnly i => new AttributeValue { N = i.ToUnixTimeSeconds().ToString() },
                IEnumerable<string> stringList => new AttributeValue { L = stringList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<int> intList => new AttributeValue { L = intList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<long> intList => new AttributeValue { L = intList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<uint> intList => new AttributeValue { L = intList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<ulong> intList => new AttributeValue { L = intList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<short> intList => new AttributeValue { L = intList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<ushort> intList => new AttributeValue { L = intList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<decimal> intList => new AttributeValue { L = intList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<double> intList => new AttributeValue { L = intList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<byte> intList => new AttributeValue { L = intList.Select(s => ToAttributeValue(s, propertyInfo)).ToList() },
                IEnumerable<object> objList => new AttributeValue { L = objList.Select(s => FromObject(s)).ToList() },
                string str => new AttributeValue { S = str },
                _ => FromObject(value)                
            };
        }

        private static readonly JsonSerializerOptions AttributeJsonOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static AttributeValue FromObject<T>(T value)
        {
            var json = JsonSerializer.Serialize(value, AttributeJsonOptions);
            var doc = Document.FromJson(json);
            return new AttributeValue { M = doc.ToAttributeMap() };
        }
    }
}
