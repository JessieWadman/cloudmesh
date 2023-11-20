using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace CloudMesh.Persistence.DynamoDB.Converters;

public class DynamoDBEnumToStringConverter<TEnum> : IPropertyConverter
{
    public object? FromEntry(DynamoDBEntry entry)
    {
        if (entry is DynamoDBNull)
            return default(TEnum);
        var asString = entry.AsString();
        
        if (int.TryParse(asString, out var intValue))
        {
            foreach (var value in Enum.GetValues(typeof(TEnum)))
            {
                if ((int)value == intValue)
                    return value;
            }
        }
            
        return Enum.TryParse(typeof(TEnum), entry.AsString(), true, out var result) 
            ? result 
            : default(TEnum);
    }

    public DynamoDBEntry ToEntry(object? value)
    {
        if (value is null)
            return DynamoDBNull.Null;
        return new Primitive(value.ToString());
    }
}
    
/// <summary>
/// Stores an Enum as a string in DymamoDB
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class DynamoDBEnumAsStringAttribute : DynamoDBPropertyAttribute
{
    private static Type GetConverterForEnumType(Type enumType)
    {
        return typeof(DynamoDBEnumToStringConverter<>).MakeGenericType(enumType);
    }

    public DynamoDBEnumAsStringAttribute(Type enumType) : base(GetConverterForEnumType(enumType)) { }
    public DynamoDBEnumAsStringAttribute(string attributeName, Type enumType) : base(attributeName, GetConverterForEnumType(enumType)) { }
}