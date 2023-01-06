using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace CloudMesh.Persistence.DynamoDB
{
    public class DynamoDBEnumToStringConverter<TEnum> : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
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
            return (TEnum)Enum.Parse(typeof(TEnum), entry.AsString(), true);
        }

        public DynamoDBEntry ToEntry(object value)
        {
            if (value is null)
                return DynamoDBNull.Null;
            return new Primitive(value.ToString());
        }
    }

    public class DynamoDBTransitiveNullableEnumConverter<TEnum> : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
        {
            if (entry is DynamoDBNull)
            {
                return null;
            }

            if (!Enum.TryParse(typeof(TEnum), entry.AsString(), true, out var result))
            {
                result = entry.AsInt(); // Transition from int values
            }

            return result;
        }

        public DynamoDBEntry ToEntry(object value)
        {
            if (value == null)
            {
                return DynamoDBNull.Null;
            }

            return new Primitive(value.ToString());
        }
    }

    public class DynamoDBNullableIntConverter : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
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

        public DynamoDBEntry ToEntry(object value)
        {
            if (value == null)
            {
                return DynamoDBNull.Null;
            }

            return Convert.ToInt32(value);
        }
    }

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
            if ((int) value == 0)
            {
                return DynamoDBNull.Null;
            }

            return Convert.ToInt32(value);
        }
    }

    public class DynamoDBTimeOnlyToStringConverter : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
        {
            TimeOnly timeOnly;

            if (entry is DynamoDBNull)
            {
                timeOnly = default(TimeOnly);
            }
            else
            {
                timeOnly = TimeOnly.Parse(entry.AsString());
            }

            return timeOnly;
        }

        public DynamoDBEntry ToEntry(object value)
        {
            if (TimeOnly.Parse(value.ToString()) == default(TimeOnly))
            {
                return DynamoDBNull.Null;
            }

            return new Primitive(value.ToString());
        }
    }
}
