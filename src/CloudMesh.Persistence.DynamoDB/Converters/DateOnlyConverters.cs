using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using System.Globalization;

namespace CloudMesh.Persistence.DynamoDB.Converters
{
    // Converts the complex type DateOnly to int64 and vice-versa.
    public class DateOnlyToLongConverter : IPropertyConverter
    {
        public DynamoDBEntry ToEntry(object value)
        {
            if (value is null)
                return DynamoDBNull.Null;

            if (value is long lng)
                return new Primitive(lng.ToString(CultureInfo.InvariantCulture), true);

            if (value is not DateOnly dateOnly)
                throw new ArgumentOutOfRangeException(nameof(value));

            lng = dateOnly.ToUnixTimeSeconds();
            if (dateOnly == DateOnly.MinValue)
                lng = 0;
            return new Primitive(lng.ToString(), true);
        }

        public object FromEntry(DynamoDBEntry entry)
        {
            if (entry is DynamoDBNull)
                return null;

            if (entry is not Primitive primitive)
                throw new ArgumentOutOfRangeException(nameof(entry));
            var value = primitive.AsLong();
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(value).Date);
        }
    }

    // Converts the complex type DateOnly to string and vice-versa.
    public class DateOnlyToStringConverter : IPropertyConverter
    {
        public DynamoDBEntry ToEntry(object value)
        {
            if (value is null)
                return DynamoDBNull.Null;

            if (value is string str)
            {
                if (DateOnly.TryParse(str, out var dateOnlyFromStr))
                    value = dateOnlyFromStr;
                else if (long.TryParse(str, out var dateOnlyFromLong))
                    value = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(dateOnlyFromLong).Date);
            }
            else if (value is long l)
                value = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(l).Date);

            if (value is not DateOnly dateOnly)
                throw new ArgumentOutOfRangeException(nameof(value));

            return new Primitive(dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), false);
        }

        public object FromEntry(DynamoDBEntry entry)
        {
            if (entry is DynamoDBNull)
                return null;

            if (entry is not Primitive primitive)
                throw new ArgumentOutOfRangeException(nameof(entry));
            var value = primitive.AsString();

            if (string.IsNullOrEmpty(value))
                return null;
            if (!DateOnly.TryParse(value, out var temp))
                return null;
            return temp;
        }
    }

    public class DynamoDBDateOnlyAsNumberAttribute : DynamoDBPropertyAttribute
    {
        public DynamoDBDateOnlyAsNumberAttribute() : base(typeof(DateOnlyToLongConverter)) { }
        public DynamoDBDateOnlyAsNumberAttribute(string attributeName) : base(attributeName, typeof(DateOnlyToLongConverter)) { }
    }

    public class DynamoDBDateOnlyAsStringAttribute : DynamoDBPropertyAttribute
    {
        public DynamoDBDateOnlyAsStringAttribute() : base(typeof(DateOnlyToStringConverter)) { }
        public DynamoDBDateOnlyAsStringAttribute(string attributeName) : base(attributeName, typeof(DateOnlyToStringConverter)) { }
    }
}
