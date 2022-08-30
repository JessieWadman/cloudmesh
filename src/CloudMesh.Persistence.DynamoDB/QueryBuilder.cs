﻿using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using CloudMesh.Persistence.DynamoDB.Helpers;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace CloudMesh.Persistence.DynamoDB
{
    public class QueryBuilder<T> : IQueryBuilder<T>
    {
        private readonly DynamoDBContext context;
        private readonly Func<DynamoDBOperationConfig> config;
        private string indexName;
        private bool reverse;
        private ConditionalOperatorValues conditionalOp = ConditionalOperatorValues.And;
        public bool consistentRead;

        private DynamoDBValue partitionKey;
        private QueryOperator queryOp;
        private DynamoDBValue[] sortKeyValues;
        private List<ScanCondition> queryFilter = new();

        public QueryBuilder(DynamoDBContext context, Func<DynamoDBOperationConfig> config)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public IQueryBuilder<T> WithQueryFilter<R>(Expression<Func<T, R>> property, ScanOperator op, params R[] values)
        {
            this.queryFilter.Add(
                new ScanCondition(
                    ExpressionHelper.GetPropertyInfo(property).Name, 
                    op, 
                    values.Cast<object>().ToArray()));
            return this;
        }

        public IQueryBuilder<T> UseIndex(string indexName)
        {
            if (consistentRead)
                throw new InvalidOperationException("Cannot use consistent read on global secondary indexes");
            this.indexName = indexName;
            return this;
        }

        public IQueryBuilder<T> Reverse()
        {
            this.reverse = true;
            return this;
        }

        public IQueryBuilder<T> UseConsistentRead()
        {
            if (!string.IsNullOrEmpty(indexName))
                throw new InvalidOperationException("Cannot use consistent read on global secondary indexes");
            this.consistentRead = true;
            return this;
        }

        public IQueryBuilder<T> WithHashKey(DynamoDBValue partitionKey)
        {
            this.partitionKey = partitionKey;
            return this;
        }

        public IQueryBuilder<T> WithSortKey(QueryOperator queryOp, params DynamoDBValue[] sortKeyValues)
        {
            this.queryOp = queryOp;
            this.sortKeyValues = sortKeyValues;
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

            if (queryFilter != null && queryFilter.Count > 0)
                opConfig.QueryFilter = this.queryFilter;

            AsyncSearch<T> search;
            if (sortKeyValues != null && sortKeyValues.Length > 0)
                search = this.context.QueryAsync<T>(partitionKey.Value, queryOp, sortKeyValues.Select(v => v.Value), opConfig);
            else
                search = this.context.QueryAsync<T>(partitionKey.Value, opConfig);

            return search;
        }

        public async IAsyncEnumerable<T> ToAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var search = CreateSearch();
            
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
