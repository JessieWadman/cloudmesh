using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace CloudMesh.Persistence.DynamoDB.Converters;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class DynamoDBNullableIntAttribute : DynamoDBPropertyAttribute
{
    public DynamoDBNullableIntAttribute() : base(typeof(DynamoDBNullableIntConverter)) { }
    public DynamoDBNullableIntAttribute(string attributeName) : base(attributeName, typeof(DynamoDBNullableIntConverter)) { }
}

public class DynamoDBNullableIntConverter : IPropertyConverter
{
    public object? FromEntry(DynamoDBEntry entry)
    {
        int? nullableInt;

        if (entry is DynamoDBNull || entry.AsString() == "null")
        {
            nullableInt = null;
        }
        else
        {
            nullableInt = entry.AsInt();
        }

        return nullableInt;
    }

    public DynamoDBEntry ToEntry(object? value)
    {
        if (value == null)
        {
            return DynamoDBNull.Null;
        }

        return Convert.ToInt32(value);
    }
}
