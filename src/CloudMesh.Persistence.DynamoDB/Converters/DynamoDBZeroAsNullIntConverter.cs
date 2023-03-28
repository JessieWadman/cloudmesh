using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace CloudMesh.Persistence.DynamoDB.Converters;

public class DynamoDBZeroAsNullIntConverter : IPropertyConverter
{
    public object FromEntry(DynamoDBEntry entry)
    {
        int? nullableInt;

        if (entry is DynamoDBNull || entry.AsInt() == 0)
        {
            nullableInt = null;
        }
        else
        {
            nullableInt = entry.AsInt();
        }

        return nullableInt;
    }

    public DynamoDBEntry ToEntry(object value)
    {
        if ((int)value == 0)
        {
            return DynamoDBNull.Null;
        }

        return Convert.ToInt32(value);
    }
}