using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace CloudMesh.Persistence.DynamoDB.Converters
{
    public class DynamoDBUnixSecondsAttribute : DynamoDBPropertyAttribute
    {
        public DynamoDBUnixSecondsAttribute()
            : base(typeof(DynamoDBUnixSecondsConverter))
        {
        }

        public DynamoDBUnixSecondsAttribute(string attributeName)
            : base(attributeName, typeof(DynamoDBUnixSecondsConverter))
        {
        }
    }

    // Converts the complex type DateTimeOffset to unix time milliseconds (int64) and vice-versa.
    public class DynamoDBUnixSecondsConverter : IPropertyConverter
    {
        public DynamoDBEntry ToEntry(object value)
        {
            if (value is null)
                return new Primitive { Value = null, Type = DynamoDBEntryType.Numeric };

            if (value is string str)
            {
                if (long.TryParse(str, out var lng))
                    return new Primitive { Value = lng, Type = DynamoDBEntryType.Numeric };
                else
                    return new Primitive { Value = value, Type = DynamoDBEntryType.Numeric };
            }
            else if (value is DateTimeOffset dto)
                return new Primitive { Value = dto.ToUnixTimeSeconds(), Type = DynamoDBEntryType.Numeric };
            else if (value is DateTime dt)
                return new Primitive { Value = new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds(), Type = DynamoDBEntryType.Numeric };
            else if (value is DateOnly don)
                return new Primitive { Value = don.ToUnixTimeSeconds(), Type = DynamoDBEntryType.Numeric };

            throw new NotSupportedException("Unsupported property type!");
        }

        public object? FromEntry(DynamoDBEntry entry)
        {
            if (entry as Primitive is null || (entry as Primitive).Value is null)
                return null;

            if ((entry as Primitive).Value is string str)
            {
                if (string.IsNullOrWhiteSpace(str))
                    return null;
                if (long.TryParse(str, out var lng))
                    return DateTimeOffset.FromUnixTimeMilliseconds(lng);
                return null;
            }

            return DateTimeOffset.FromUnixTimeMilliseconds((entry as Primitive).AsLong());
        }
    }
}
