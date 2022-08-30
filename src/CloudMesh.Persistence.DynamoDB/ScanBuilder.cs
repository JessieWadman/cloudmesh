using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Models.Persistence;
using CloudMesh.Persistence.DynamoDB.Helpers;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace CloudMesh.Persistence.DynamoDB
{
    public class ScanBuilder<T> : IScanBuilder<T>
    {
        private readonly IDynamoDBContext context;
        private readonly Func<DynamoDBOperationConfig> config;
        private readonly HashSet<ScanFilter> filters = new();
        private string indexName;
        private bool reverse;
        private ConditionalOperatorValues conditionalOp = ConditionalOperatorValues.And;
        public bool consistentRead;

        public ScanBuilder(IDynamoDBContext context, Func<DynamoDBOperationConfig> config)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public IScanBuilder<T> UseIndex(string indexName)
        {
            this.indexName = indexName;
            return this;
        }

        public IScanBuilder<T> UseConsistentRead()
        {
            this.consistentRead = true;
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
                var propAttrib = ExpressionHelper.GetDynamoDBattribute(prop);
                var asLong = propAttrib is not null && propAttrib.Converter == typeof(DateOnlyToLongConverter);

                var temp = ToObjectArray(
                    values.Cast<DateOnly>(), 
                    d => asLong ? d.ToUnixTimeSeconds() : d.ToString());
                    
                filters.Add(new ScanFilter
                {
                    Property = ExpressionHelper.GetPropertyInfo(property).Name,
                    Operator = scanOp,
                    Values = temp
                });
            }
            else
            {
                filters.Add(new ScanFilter
                {
                    Property = ExpressionHelper.GetPropertyInfo(property).Name,
                    Operator = scanOp,
                    Values = values.Cast<object>().ToArray()
                });
            }
            return this;
        }

        public IScanBuilder<T> Where<R>(Expression<Func<T, IEnumerable<R>>> property, ScanOperator scanOp, R value)
        {
            if (!(new ScanOperator[] { ScanOperator.Contains, ScanOperator.NotContains }).Contains(scanOp))
                throw new ArgumentException("Not a supported scan operator on sets");

            if (typeof(R).IsAssignableFrom(typeof(DateOnly)))
            {
                var prop = ExpressionHelper.GetPropertyInfo(property);
                var asLong = ExpressionHelper.GetDynamoDBattributes(prop)
                    .Any(propAttr => propAttr is not null && propAttr.Converter == typeof(DateOnlyToLongConverter));

                object temp = value;
                if (value is DateOnly dateOnly)
                    temp = asLong ? dateOnly.ToUnixTimeSeconds() : dateOnly.ToString();

                filters.Add(new ScanFilter
                {
                    Property = ExpressionHelper.GetPropertyInfo(property).Name,
                    Operator = scanOp,
                    Values = new object[] { temp }
                });
            }
            else
            {
                filters.Add(new ScanFilter
                {
                    Property = ExpressionHelper.GetPropertyInfo(property).Name,
                    Operator = scanOp,
                    Values = new object[] { value }
                });
            }
            return this;
        }

        public IScanBuilder<T> Reverse()
        {
            this.reverse = true;
            return this;
        }

        public IScanBuilder<T> UseOrInsteadOfAnd()
        {
            this.conditionalOp = ConditionalOperatorValues.Or;
            return this;
        }

        private AsyncSearch<T> CreateSearch()
        {
            var opConfig = config();
            if (reverse)
                opConfig.BackwardQuery = true;
            opConfig.ConditionalOperator = this.conditionalOp;
            opConfig.ConsistentRead = this.consistentRead;
            
            if (!string.IsNullOrWhiteSpace(indexName))
                opConfig.IndexName = indexName;

            var scanOps = filters.Select(filter => new ScanCondition(filter.Property, filter.Operator, filter.Values)).ToArray();

            var search = this.context.ScanAsync<T>(scanOps, opConfig);
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
