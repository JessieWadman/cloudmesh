using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using CloudMesh.Persistence.DynamoDB.Helpers;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using CloudMesh.Persistence.DynamoDB.Converters;
using System.Diagnostics;

namespace CloudMesh.Persistence.DynamoDB.Builders
{
    [DebuggerDisplay("{ToString()}")]
    public class ScanFilter
    {
        public ScanFilter(string property, ScanOperator scanOperator, object[] values)
        {
            Property = property;
            Operator = scanOperator;
            Values = values;
        }
        
        public string Property { get; init; }
        public ScanOperator Operator { get; init; }
        public object[] Values { get; init; }

        public static ScanFilter With<T, P>(Expression<Func<T, P>> property, ScanOperator scanOperator, params P[] values)
        {
            return new ScanFilter(ExpressionHelper.GetPropertyInfo(property).Name,
                scanOperator,
                values.Cast<object>().ToArray());
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
                _ => throw new NotSupportedException("The requested scan operator is not supported")
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
    public class ScanBuilder<T> : IScanBuilder<T>
    {
        private readonly IDynamoDBContext context;
        private readonly Func<DynamoDBOperationConfig> config;
        private readonly HashSet<ScanFilter> filters = new();
        private string? indexName;
        private bool reverse;
        private ConditionalOperatorValues conditionalOp = ConditionalOperatorValues.And;
        private bool consistentRead;

        public ScanBuilder(IDynamoDBContext context, Func<DynamoDBOperationConfig> config)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public IScanBuilder<T> UseIndex(string indexName)
        {
            ArgumentNullException.ThrowIfNull(indexName);
            this.indexName = indexName;
            return this;
        }

        public IScanBuilder<T> UseConsistentRead()
        {
            consistentRead = true;
            return this;
        }

        private static object[] ToObjectArray<R>(IEnumerable<R> values, Func<R, object> transformer)
        {
            return values.Select(transformer).ToArray();
        }

        public IScanBuilder<T> Where<R>(Expression<Func<T, R>> property, ScanOperator scanOp, params R[] values)
        {
            if (typeof(R).IsAssignableFrom(typeof(DateOnly)))
            {
                var prop = ExpressionHelper.GetPropertyInfo(property);
                var propAttrib = ExpressionHelper.GetDynamoDBAttribute(prop);
                var asLong = propAttrib is not null && propAttrib.Converter == typeof(DateOnlyToLongConverter);

                var temp = ToObjectArray(
                    values.Cast<DateOnly>(),
                    d => asLong ? d.ToUnixTimeSeconds() : d.ToString());

                filters.Add(new ScanFilter(
                    ExpressionHelper.GetPropertyInfo(property).Name,
                    scanOp,
                    temp));
            }
            else
            {
                filters.Add(new ScanFilter(
                    ExpressionHelper.GetPropertyInfo(property).Name,
                    scanOp,
                    values.Cast<object>().ToArray()
                ));
            }
            return this;
        }

        public IScanBuilder<T> Where<R>(Expression<Func<T, IEnumerable<R>>> property, ScanOperator scanOp, R value)
        {
            if (!(new[] { ScanOperator.Contains, ScanOperator.NotContains }).Contains(scanOp))
                throw new ArgumentException("Not a supported scan operator on sets");

            if (typeof(R).IsAssignableFrom(typeof(DateOnly)))
            {
                var prop = ExpressionHelper.GetPropertyInfo(property);
                var asLong = ExpressionHelper.GetDynamoDBAttributes(prop)
                    .Any(propAttr => propAttr.Converter == typeof(DateOnlyToLongConverter));

                object temp = value!;
                if (value is DateOnly dateOnly)
                    temp = asLong ? dateOnly.ToUnixTimeSeconds() : dateOnly.ToString();

                filters.Add(new ScanFilter(
                    ExpressionHelper.GetPropertyInfo(property).Name,
                    scanOp,
                    new[] { temp }
                ));
            }
            else
            {
                filters.Add(new ScanFilter(
                    ExpressionHelper.GetPropertyInfo(property).Name,
                    scanOp,
                    new object[] { value! }
                ));
            }
            return this;
        }

        public IScanBuilder<T> Reverse()
        {
            reverse = true;
            return this;
        }

        public IScanBuilder<T> UseOrInsteadOfAnd()
        {
            conditionalOp = ConditionalOperatorValues.Or;
            return this;
        }

        private AsyncSearch<T> CreateSearch()
        {
            var opConfig = config();
            if (reverse)
                opConfig.BackwardQuery = true;
            opConfig.ConditionalOperator = conditionalOp;
            opConfig.ConsistentRead = consistentRead;

            if (!string.IsNullOrWhiteSpace(indexName))
                opConfig.IndexName = indexName;

            var scanOps = filters.Select(filter => new ScanCondition(filter.Property, filter.Operator, filter.Values)).ToArray();

            var search = context.ScanAsync<T>(scanOps, opConfig);
            return search;
        }

        public async IAsyncEnumerable<T> ToAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            AsyncSearch<T> search = CreateSearch();
            while (!search.IsDone)
            {
                var page = await search.GetNextSetAsync(cancellationToken);
                foreach (var item in page)
                    yield return item;
            }
        }

        public async Task<T[]> ToArrayAsync(CancellationToken cancellationToken)
        {
            var search = CreateSearch();
            var items = await search.GetRemainingAsync(cancellationToken);
            return items.ToArray();
        }
    }
}
