using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using System;

namespace CloudMesh.Persistence.DynamoDB.Converters;

/// <summary>
/// Stores a DateTimeOffset as an ISO8601 string
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class DynamoDBISO8601DateTimeOffsetAttribute : DynamoDBPropertyAttribute
{
    public DynamoDBISO8601DateTimeOffsetAttribute() : base(typeof(DynamoDBISO8601DateTimeOffsetConverter)) { }
    public DynamoDBISO8601DateTimeOffsetAttribute(string attributeName) : base(attributeName, typeof(DynamoDBISO8601DateTimeOffsetConverter)) { }
}

// Converts the complex type DateTimeOffset to string and vice-versa.
public class DynamoDBISO8601DateTimeOffsetConverter : IPropertyConverter
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