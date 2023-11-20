using System.Globalization;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace CloudMesh.Persistence.DynamoDB.Converters;

/// <summary>
/// Stores a DateOnly as int64 number
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class DynamoDBDateOnlyAsNumberAttribute : DynamoDBPropertyAttribute
{
    public DynamoDBDateOnlyAsNumberAttribute() : base(typeof(DateOnlyToLongConverter)) { }
    public DynamoDBDateOnlyAsNumberAttribute(string attributeName) : base(attributeName, typeof(DateOnlyToLongConverter)) { }
}

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

    public object? FromEntry(DynamoDBEntry entry)
    {
        if (entry is DynamoDBNull)
            return null;

        if (entry is not Primitive primitive)
            throw new ArgumentOutOfRangeException(nameof(entry));
        var value = primitive.AsLong();
        return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(value).Date);
    }
}

