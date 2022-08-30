using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using CloudMesh.Persistence.DynamoDB.Helpers;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;

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

    [DebuggerDisplay("{ToString()}")]
    public class ScanFilter
    {
        public string Property { get; init; }
        public ScanOperator Operator { get; init; }
        public object[] Values { get; init; }

        public static ScanFilter With<T, P>(Expression<Func<T, P>> property, ScanOperator scanOperator, params P[] values)
        {
            return new ScanFilter
            {
                Property = ExpressionHelper.GetPropertyInfo(property).Name,
                Operator = scanOperator,
                Values = values.Cast<object>().ToArray()
            };
        }

        public string ToString(params string[] argNames)
        {
            if (argNames.Length == 0)
                throw new ArgumentNullException(nameof(argNames));

            return Operator switch
            {
                ScanOperator.Equal => $"{Property} = {argNames[0]}",
                ScanOperator.NotEqual => $"{Property} <> {argNames[0]}",
                ScanOperator.LessThan => $"{Property} < {argNames[0]}",
                ScanOperator.GreaterThan => $"{Property} > {argNames[0]}",
                ScanOperator.LessThanOrEqual => $"{Property} <= {argNames[0]}",
                ScanOperator.GreaterThanOrEqual => $"{Property} >= {argNames[0]}",
                ScanOperator.BeginsWith => $"begins_with({Property}, {argNames[0]})",
                ScanOperator.Contains => $"contains({Property}, {argNames[0]})",
                ScanOperator.NotContains => $"not_contains({Property}, {argNames[0]})",
                ScanOperator.Between => $"{Property} between {argNames[0]} AND {argNames[1]}",
                ScanOperator.In => $"{Property} IN ({string.Join(", ", argNames)})",
                ScanOperator.IsNull => $"attribute_type({Property}, NULL)",
                ScanOperator.IsNotNull => $"NOT attribute_type({Property}, NULL)",
                _ => throw new NotSupportedException("The requested scan operator is not supporte")
            };
        }

        public override string ToString() => ToString(":arg1");
    }

    public interface IScanBuilder<T>
    {
        IScanBuilder<T> UseIndex(string indexName);
        IScanBuilder<T> Where<R>(Expression<Func<T, R>> property, ScanOperator scanOp, params R[] values);
        IScanBuilder<T> Where<R>(Expression<Func<T, IEnumerable<R>>> property, ScanOperator scanOp, R value);
        IScanBuilder<T> Reverse();
        IScanBuilder<T> UseOrInsteadOfAnd();
        IScanBuilder<T> UseConsistentRead();
        IAsyncEnumerable<T> ToAsyncEnumerable(CancellationToken cancellationToken);
        Task<T[]> ToArrayAsync(CancellationToken cancellationToken);
    }

    public interface IQueryBuilder<T>
    {
        IQueryBuilder<T> UseIndex(string indexName);
        IQueryBuilder<T> Reverse();
        IQueryBuilder<T> UseConsistentRead();
        IQueryBuilder<T> WithHashKey(DynamoDBValue partitionKey);
        IQueryBuilder<T> WithSortKey(QueryOperator queryOp, params DynamoDBValue[] value);
        IAsyncEnumerable<T> ToAsyncEnumerable(CancellationToken cancellationToken);
        Task<T[]> ToArrayAsync(CancellationToken cancellationToken);
        IQueryBuilder<T> WithQueryFilter<R>(Expression<Func<T, R>> property, ScanOperator op, params R[] values);

    }

    public interface IBatchWriteBuilder<T>
    {
        IBatchWriteBuilder<T> Save(params T[] items);
        IBatchWriteBuilder<T> Delete(params T[] items);
        ValueTask ExecuteAsync(CancellationToken cancellationToken);
    }

    public interface IRepositoryFactory
    {
        IRepository<T> For<T>(string tableName);
    }

    public enum PatchCondition
    {
        Equals,
        NotEquals,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        StartsWith
    }

    public interface IPatchBuilder<T>
    {
        IPatchBuilder<T> If<R>(Expression<Func<T, R>> property, PatchCondition condition, R value);
        IPatchBuilder<T> IfContains<R>(Expression<Func<T, IEnumerable<R>>> property, R value);
        IPatchBuilder<T> IfSize<R>(Expression<Func<T, R>> property, PatchCondition condition, int value);
        IPatchBuilder<T> Remove<R>(Expression<Func<T, R>> property);
        IPatchBuilder<T> Set<R>(Expression<Func<T, R>> property, R value);
        IPatchBuilder<T> Increment<R>(Expression<Func<T, R>> property, R incrementBy);
        IPatchBuilder<T> Decrement<R>(Expression<Func<T, R>> property, R incrementBy);
        IPatchBuilder<T> Add<R>(Expression<Func<T, IEnumerable<R>>> property, params R[] elements);
        IPatchBuilder<T> With<R>(R value);
        ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken);
        ValueTask<T> ExecuteAndGetAsync(CancellationToken cancellationToken);
    }

    public interface IRepository<T> : IDisposable
    {
        ValueTask<T> GetById(DynamoDBValue hashKey, CancellationToken cancellationToken);
        ValueTask<T> GetById(DynamoDBValue hashKey, DynamoDBValue rangeKey, CancellationToken cancellationToken);
        ValueTask<T> GetById(string indexName, DynamoDBValue hashKey, CancellationToken cancellationToken);
        ValueTask<T> GetById(string indexName, DynamoDBValue hashKey, DynamoDBValue rangeKey, CancellationToken cancellationToken);
        ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params DynamoDBValue[] hashKeys);
        ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys);
        ValueTask<T[]> GetByIds(string indexName, CancellationToken cancellationToken, params DynamoDBValue[] hashKeys);
        ValueTask<T[]> GetByIds(string indexName, CancellationToken cancellationToken, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys);

        ValueTask SaveAsync(T item, CancellationToken cancellationToken);
        ValueTask SaveAsync(IEnumerable<T> items, CancellationToken cancellationToken);
        ValueTask DeleteAsync(T item, CancellationToken cancellationToken);
        ValueTask DeleteAsync(CancellationToken cancellationToken, params T[] items);
        ValueTask DeleteAsync(DynamoDBValue id, CancellationToken cancellationToken);
        ValueTask DeleteAsync(DynamoDBValue hashKey, DynamoDBValue sortKey, CancellationToken cancellationToken);
        IBatchWriteBuilder<T> BatchWrite();
        IQueryBuilder<T> Query();
        IScanBuilder<T> Scan();
        IPatchBuilder<T> Patch(DynamoDBValue hashKey);
        IPatchBuilder<T> Patch(DynamoDBValue hashKey, DynamoDBValue rangeKey);
    }
}
