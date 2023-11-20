using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace CloudMesh.Persistence.DynamoDB.Converters;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class DynamoDBTimeOnlyAsStringAttribute : DynamoDBPropertyAttribute
{
    public DynamoDBTimeOnlyAsStringAttribute() : base(typeof(DynamoDBTimeOnlyToStringConverter)) { }
    public DynamoDBTimeOnlyAsStringAttribute(string attributeName) : base(attributeName, typeof(DynamoDBTimeOnlyToStringConverter)) { }
}

public class DynamoDBTimeOnlyToStringConverter : IPropertyConverter
{
    public object FromEntry(DynamoDBEntry entry)
    {
        TimeOnly timeOnly;

        timeOnly = entry is DynamoDBNull 
            ? default 
            : TimeOnly.Parse(entry.AsString());

        return timeOnly;
    }

    public DynamoDBEntry ToEntry(object? value)
    {
        if (value is null || !TimeOnly.TryParse(value.ToString(), out _))
            return DynamoDBNull.Null;

        return new Primitive(value.ToString());
    }
}