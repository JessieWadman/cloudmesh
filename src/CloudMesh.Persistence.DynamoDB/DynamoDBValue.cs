using Amazon.DynamoDBv2.Model;
using System.Globalization;

namespace CloudMesh.Persistence.DynamoDB
{
    public enum DynamoDBValueType
    {
        String,
        Number,
        Boolean,
        Null
    }

    public readonly struct DynamoDBValue
    {
        private readonly object Original { get; init; }
        public object Value { get; init; }
        public DynamoDBValueType ValueType { get; init; }
        public static readonly DynamoDBValue Null = new() { Original = null, ValueType = DynamoDBValueType.Null };

        public static implicit operator DynamoDBValue(Enum value) => new() {  Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(string value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.String };
        public static implicit operator DynamoDBValue(short value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(ushort value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(byte value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(double value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(decimal value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(float value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(uint value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(int value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(long value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(ulong value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(Guid value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.String };
        public static implicit operator DynamoDBValue(bool value) => new() { Original = value, Value = value, ValueType = DynamoDBValueType.Boolean };
        public static implicit operator DynamoDBValue(DateOnly value) => new() { Original = value, Value = value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ValueType = DynamoDBValueType.Number };
        public static implicit operator DynamoDBValue(DateTime value) => new() { Original = value, Value = value.ToString(CultureInfo.InvariantCulture), ValueType = DynamoDBValueType.String };
        public static implicit operator DynamoDBValue(DateTimeOffset value) => new() { Original = value, Value = value.ToISO8601(), ValueType = DynamoDBValueType.String };

        public static DynamoDBValue From<T>(T value)
        {
            return value switch
            {
                string v => v,
                Guid v => v,
                byte v => v,
                short v => v,
                ushort v => v,
                int v => v,
                uint v => v,
                long v => v,
                ulong v => v,
                decimal v => v,
                double v => v,
                float v => v,
                bool v => v,
                DateOnly v => v,
                DateTime v => v,
                DateTimeOffset v => v,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
            };
        }

        public object ToObject() => Original;
        public AttributeValue ToAttributeValue()
        {
            return ValueType switch
            {
                DynamoDBValueType.String => new AttributeValue { S = Value.ToString() },
                DynamoDBValueType.Boolean => new AttributeValue { BOOL = (bool)Value },
                DynamoDBValueType.Number => new AttributeValue { N = Convert.ToString(Value, CultureInfo.InvariantCulture) },
                DynamoDBValueType.Null => new AttributeValue { N = (string)Value },
                _ => throw new InvalidCastException()
            };
        }
    }
}
