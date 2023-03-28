using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using System.Globalization;

namespace CloudMesh.Persistence.DynamoDB.Converters;

/// <summary>
/// Stores DateOnly property as a string in DynamoDB
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class DynamoDBDateOnlyAsStringAttribute : DynamoDBPropertyAttribute
{
    public DynamoDBDateOnlyAsStringAttribute() : base(typeof(DateOnlyToStringConverter)) { }
    public DynamoDBDateOnlyAsStringAttribute(string attributeName) : base(attributeName, typeof(DateOnlyToStringConverter)) { }
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

