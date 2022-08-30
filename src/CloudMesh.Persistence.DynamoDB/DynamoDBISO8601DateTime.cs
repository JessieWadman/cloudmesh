using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using System;

namespace CloudMesh.Persistence.DynamoDB
{
    public class DynamoDBDateTimeAttribute : DynamoDBPropertyAttribute
    {
        public DynamoDBDateTimeAttribute()
            : base(typeof(ISO8601DateTimeConverter))
        {
        }

        public DynamoDBDateTimeAttribute(string attributeName) 
            : base(attributeName, typeof(ISO8601DateTimeConverter))
        {
        }
    }

    // Converts the complex type DateTimeOffset to string and vice-versa.
    public class ISO8601DateTimeConverter : IPropertyConverter
    {
        public DynamoDBEntry ToEntry(object value)
        {
            if (value is null)
                return new Primitive { Value = null, Type = DynamoDBEntryType.String };

            if (value is string)
                return new Primitive { Value = value };

            else if (value is DateTimeOffset dt)
                return new Primitive { Value = dt.ToISO8601() };

            throw new NotSupportedException("Unsupported property type!");
        }

        public object? FromEntry(DynamoDBEntry entry)
        {
            if (entry is not Primitive primitive || primitive.Value is not string str || string.IsNullOrEmpty(str))
                return null;

            return DateHelper.FromISO8601String(entry.AsString());
        }
    }
}
