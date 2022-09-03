using Amazon.DynamoDBv2.DocumentModel;
using CloudMesh.Persistence.DynamoDB.Helpers;
using CloudMesh.Serialization.Json;
using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Expression = System.Linq.Expressions.Expression;

namespace CloudMesh.Persistence.DynamoDB.Mock
{
    public class InMemoryRepositoryFactory : IRepositoryFactory
    {
        private readonly Dictionary<string, List<string>> tables = new();

        public IRepository<T> For<T>(string tableName)
        {
            if (!tables.TryGetValue(tableName, out var rows))
                rows = tables[tableName] = new List<string>();

            return new InMemoryRepository<T>(rows);
        }
    }

    public class InMemoryRepository<T> : IRepository<T>
    {
        private readonly List<string> _rows;
        public IQueryable<T> Rows => this._rows.Select(row => JsonSerializer.Deserialize<T>(row)).AsQueryable();

        public InMemoryRepository(List<string> rows)
        {
            this._rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }

        public ValueTask<T> GetById(DynamoDBValue hashKey, CancellationToken cancellationToken)
        {
            var row = Rows.Where(ExpressionHelper.CreateHashKeyPredicate<T>(hashKey)) // .Where(t => t.Id == "Test")
                .FirstOrDefault();
            return new ValueTask<T>(row);
        }

        public ValueTask<T> GetById(DynamoDBValue hashKey, DynamoDBValue rangeKey, CancellationToken cancellationToken)
        {
            var predicate = ExpressionHelper.CreatePredicate<T>(
                (ExpressionHelper.GetHashKeyProperty<T>(), hashKey.Value),
                (ExpressionHelper.GetRangeKeyProperty<T>(), rangeKey.Value));

            var row = Rows
                .Where(predicate)
                .FirstOrDefault();

            return new ValueTask<T>(row);
        }

        public ValueTask<T> GetById(string indexName, DynamoDBValue hashKey, CancellationToken cancellationToken)
        {
            var row = Rows.Where(ExpressionHelper.CreateGlobalSecondaryHashKeyPredicate<T>(indexName, hashKey))
                .FirstOrDefault();
            return new ValueTask<T>(row);
        }

        public ValueTask<T> GetById(string indexName, DynamoDBValue hashKey, DynamoDBValue rangeKey, CancellationToken cancellationToken)
        {
            var predicate = ExpressionHelper.CreatePredicate<T>(
                (ExpressionHelper.GetGlobalSecondaryHashKeyProperty<T>(indexName), hashKey.Value),
                (ExpressionHelper.GetGlobalSecondaryIndexRangeKeyProperty<T>(indexName), rangeKey.Value));

            var row = Rows
                .Where(predicate)
                .FirstOrDefault();

            return new ValueTask<T>(row);
        }

        public async ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params DynamoDBValue[] hashKeys)
        {
            var result = new HashSet<T>();
            foreach (var hashKey in hashKeys)
            {
                var item = await GetById(hashKey, cancellationToken);
                if (item is not null)
                    result.Add(item);
            }
            return result.ToArray();
        }

        public async ValueTask<T[]> GetByIds(CancellationToken cancellationToken, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys)
        {
            var result = new HashSet<T>();
            foreach (var (hashKey, rangeKey) in keys)
            {
                var item = await GetById(hashKey, rangeKey, cancellationToken);
                if (item is not null)
                    result.Add(item);
            }
            return result.ToArray();
        }

        public async ValueTask<T[]> GetByIds(string indexName, CancellationToken cancellationToken, params DynamoDBValue[] hashKeys)
        {
            var result = new HashSet<T>();
            foreach (var hashKey in hashKeys)
            {
                var item = await GetById(indexName, hashKey, cancellationToken);
                if (item is not null)
                    result.Add(item);
            }
            return result.ToArray();
        }

        public async ValueTask<T[]> GetByIds(string indexName, CancellationToken cancellationToken, params (DynamoDBValue HashKey, DynamoDBValue RangeKey)[] keys)
        {
            var result = new HashSet<T>();
            foreach (var (hashKey, rangeKey) in keys)
            {
                var item = await GetById(indexName, hashKey, rangeKey, cancellationToken);
                if (item is not null)
                    result.Add(item);
            }
            return result.ToArray();
        }

        public async ValueTask SaveAsync(T item, CancellationToken cancellationToken)
        {
            var existingRows = Rows;

            var hashKeyProp = ExpressionHelper.GetHashKeyProperty<T>();
            var keyValue = hashKeyProp.GetValue(item);

            existingRows = existingRows.Where(ExpressionHelper.CreatePredicate<T>(hashKeyProp, keyValue));

            if (ExpressionHelper.HasRangeKeyProperty<T>())
            {
                var rangeKeyProp = ExpressionHelper.GetRangeKeyProperty<T>();
                if (rangeKeyProp != null)
                {
                    var sortKeyValue = rangeKeyProp.GetValue(item);

                    existingRows = existingRows.Where(ExpressionHelper.CreatePredicate<T>(rangeKeyProp, sortKeyValue));
                }
            }

            await DeleteAsync(cancellationToken, existingRows.ToArray());

            _rows.Add(JsonSerializer.Serialize(item));
        }

        public async ValueTask SaveAsync(IEnumerable<T> items, CancellationToken cancellationToken)
        {
            foreach (var item in items)
                await SaveAsync(item, cancellationToken);
        }

        public ValueTask DeleteAsync(T item, CancellationToken cancellationToken)
        {
            var rowsSnapshot = Rows.ToArray();

            var hashKeyProp = ExpressionHelper.GetHashKeyProperty<T>();
            var keyValue = hashKeyProp.GetValue(item);
            var existingRows = rowsSnapshot.AsQueryable().Where(ExpressionHelper.CreatePredicate<T>(hashKeyProp, keyValue)).ToArray();
            var indexes = new List<int>();

            foreach (var row in existingRows)
            {
                var index = Array.IndexOf(rowsSnapshot, row);
                if (index >= 0)
                    indexes.Add(index);
            }

            foreach (var index in indexes.OrderByDescending(i => i))
                _rows.RemoveAt(index);

            return ValueTask.CompletedTask;
        }

        public async ValueTask DeleteAsync(CancellationToken cancellationToken, params T[] items)
        {
            foreach (var item in items)
                await DeleteAsync(item, cancellationToken);
        }

        public async ValueTask DeleteAsync(DynamoDBValue id, CancellationToken cancellationToken)
        {
            var hashKeyProp = ExpressionHelper.GetHashKeyProperty<T>();
            var existingRows = Rows.Where(ExpressionHelper.CreatePredicate<T>(hashKeyProp, id.Value)).ToArray();
            foreach (var row in existingRows)
                await DeleteAsync(row, cancellationToken);
        }

        public async ValueTask DeleteAsync(DynamoDBValue hashKey, DynamoDBValue sortKey, CancellationToken cancellationToken)
        {
            var hashKeyProp = ExpressionHelper.GetHashKeyProperty<T>();
            var rangeKeyProp = ExpressionHelper.GetRangeKeyProperty<T>();
            var existingRows = Rows
                .Where(ExpressionHelper.CreatePredicate<T>((hashKeyProp, hashKey.Value), (rangeKeyProp, sortKey.Value)))
                .ToArray();

            foreach (var row in existingRows)
                await DeleteAsync(row, cancellationToken);
        }

        public IBatchWriteBuilder<T> BatchWrite()
        {
            return new InMemoryBatchBuilder<T>(this);
        }

        public IQueryBuilder<T> Query()
        {
            return new InMemoryQueryBuilder<T>(this.Rows.ToHashSet());
        }

        public IScanBuilder<T> Scan()
        {
            return new InMemoryScanBuilder<T>(this.Rows.ToHashSet());
        }

        public void Dispose()
        {
            // NoOp
            GC.SuppressFinalize(this);
        }

        public IPatchBuilder<T> Patch(DynamoDBValue hashKey)
        {
            var getRow = GetById(hashKey, CancellationToken.None);
            Debug.Assert(getRow.IsCompleted);
            var row = getRow.Result;
            if (row is null)
                row = Activator.CreateInstance<T>();
            return new InMemoryPatchBuilder<T>(this, row, hashKey, null);
        }

        public IPatchBuilder<T> Patch(DynamoDBValue hashKey, DynamoDBValue rangeKey)
        {
            var getRow = GetById(hashKey, rangeKey, CancellationToken.None);
            Debug.Assert(getRow.IsCompleted);
            var row = getRow.Result;
            if (row is null)
                row = Activator.CreateInstance<T>();
            return new InMemoryPatchBuilder<T>(this, row, hashKey, rangeKey);
        }
    }

    public class InMemoryQueryBuilder<T> : IQueryBuilder<T>
    {
        private IQueryable<T> query;
        private string indexName;

        private DynamoDBValue? hashKey;
        private (QueryOperator, DynamoDBValue[])? rangeKeys = null;

        public InMemoryQueryBuilder(HashSet<T> source)
        {
            this.query = source.AsQueryable();
        }

        public IQueryBuilder<T> Reverse()
        {
            query = query.Reverse();
            return this;
        }

        private IQueryable<T> BuildQuery()
        {
            if (hashKey.HasValue)
                query = query.Where(ExpressionHelper.CreatePredicate<T>(indexName, hashKey.Value, rangeKeys));
            return query;
        }

        public Task<T[]> ToArrayAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(BuildQuery().ToArray());
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        //TODO: Returns empty while unit-testing.
        public async IAsyncEnumerable<T> ToAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            foreach (var item in BuildQuery())
                yield return item;
        }

        private bool consistentRead;

        public IQueryBuilder<T> UseConsistentRead()
        {
            if (!string.IsNullOrEmpty(indexName))
                throw new InvalidOperationException("Cannot use consistent read on global secondary indexes");
            consistentRead = true;
            return this;
        }

        public IQueryBuilder<T> UseIndex(string indexName)
        {
            if (consistentRead)
                throw new InvalidOperationException("Cannot use consistent read on global secondary indexes");
            this.indexName = indexName;
            return this;
        }

        public IQueryBuilder<T> WithHashKey(DynamoDBValue hashKey)
        {
            this.hashKey = hashKey;
            return this;
        }

        public IQueryBuilder<T> WithSortKey(QueryOperator queryOp, params DynamoDBValue[] values)
        {
            this.rangeKeys = (queryOp, values);
            return this;
        }

        public IQueryBuilder<T> WithQueryFilter<R>(Expression<Func<T, R>> property, ScanOperator op, params R[] values)
        {
            //TODO: implement
            return this;
        }
    }

    public class InMemoryScanBuilder<T> : IScanBuilder<T>
    {
        private IQueryable<T> query;
        private bool useOrInsteadOfAnd = false;
        private readonly HashSet<Expression> whereClauses = new();
        private readonly ParameterExpression parameter = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");

        public InMemoryScanBuilder(HashSet<T> rows)
        {
            this.query = rows.AsQueryable();
        }

        public IScanBuilder<T> Reverse()
        {
            query = query.Reverse();
            return this;
        }

        private IQueryable<T> BuildQuery()
        {
            var query = this.query;
            if (whereClauses.Count > 0)
            {
                Expression condition = null;
                foreach (var whereClause in whereClauses)
                {
                    if (condition is null)
                        condition = whereClause;
                    else if (useOrInsteadOfAnd)
                        condition = Expression.Or(condition, whereClause);
                    else
                        condition = Expression.And(condition, whereClause);
                }
                var predicateLambda = Expression.Lambda<Func<T, bool>>(condition, parameter);
                query = query.Where(predicateLambda);
            }
            return query;
        }

        public Task<T[]> ToArrayAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(BuildQuery().ToArray());
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async IAsyncEnumerable<T> ToAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            foreach (var row in BuildQuery())
                yield return row;
        }

        public IScanBuilder<T> UseConsistentRead()
        {
            return this;
        }

        public IScanBuilder<T> UseIndex(string indexName)
        {
            return this;
        }

        public IScanBuilder<T> UseOrInsteadOfAnd()
        {
            useOrInsteadOfAnd = true;
            return this;
        }

        public IScanBuilder<T> Where<R>(Expression<Func<T, R>> property, ScanOperator scanOp, params R[] values)
        {
            var expression = ExpressionHelper.CreateScanExpression(this.parameter, property, scanOp, values);
            whereClauses.Add(expression);
            return this;
        }

        public IScanBuilder<T> Where<R>(Expression<Func<T, IEnumerable<R>>> property, ScanOperator scanOp, R value)
        {
            var expression = ExpressionHelper.CreateScanExpression<T, R>(this.parameter, property, scanOp, value);
            whereClauses.Add(expression);
            return this;
        }
    }

    public class InMemoryBatchBuilder<T> : IBatchWriteBuilder<T>
    {
        private readonly InMemoryRepository<T> repo;
        private readonly List<(bool IsDelete, T Item)> operations = new();

        public InMemoryBatchBuilder(InMemoryRepository<T> repo)
        {
            this.repo = repo;
        }

        public IBatchWriteBuilder<T> Delete(params T[] items)
        {
            foreach (var item in items)
                operations.Add((true, item));
            return this;
        }

        public IBatchWriteBuilder<T> Save(params T[] items)
        {
            foreach (var item in items)
                operations.Add((false, item));
            return this;
        }

        public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            foreach (var (isDelete, item) in operations)
                if (isDelete)
                    await repo.DeleteAsync(cancellationToken, item);
                else
                    await repo.SaveAsync(item, cancellationToken);
        }

    }

    public class InMemoryPatchBuilder<T> : IPatchBuilder<T>
    {
        private readonly InMemoryRepository<T> repo;
        private bool conditionMet = true;
        private T row;
        private readonly DynamoDBValue hashKey;
        private readonly DynamoDBValue? rangeKey;

        public InMemoryPatchBuilder(InMemoryRepository<T> repo, T row, DynamoDBValue hashKey, DynamoDBValue? rangeKey)
        {
            this.repo = repo ?? throw new ArgumentNullException(nameof(repo));
            this.row = row;
            this.hashKey = hashKey;
            this.rangeKey = rangeKey;
        }

        public IPatchBuilder<T> Add<R>(Expression<Func<T, IEnumerable<R>>> property, params R[] elements)
        {
            var prop = ExpressionHelper.GetPropertyInfo(property);
            var collection = prop.GetValue(row) as ICollection<R>;
            foreach (var item in elements)
                collection.Add(item);
            return this;
        }

        public IPatchBuilder<T> Increment<R>(Expression<Func<T, R>> property, R incrementBy)
        {
            var prop = ExpressionHelper.GetPropertyInfo(property);
            dynamic value = prop.GetValue(row);
            value += incrementBy;
            prop.SetValue(row, value);
            return this;
        }

        public IPatchBuilder<T> Decrement<R>(Expression<Func<T, R>> property, R incrementBy)
        {
            var prop = ExpressionHelper.GetPropertyInfo(property);
            dynamic value = prop.GetValue(row);
            value -= incrementBy;
            prop.SetValue(row, value);
            return this;
        }

        public IPatchBuilder<T> If<R>(Expression<Func<T, R>> property, PatchCondition condition, R value)
        {
            if (!conditionMet)
                return this;

            var query = repo.Rows;

            var hashKeyPredicate = ExpressionHelper.CreateHashKeyPredicate<T>(hashKey);
            query = query.Where(hashKeyPredicate);
            if (rangeKey.HasValue)
            {
                var rangeKeyPredicate = ExpressionHelper.CreateRangeKeyKeyPredicate<T>(rangeKey.Value);
                query = query.Where(rangeKeyPredicate);
            }

            var parameter = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
            var expr = ExpressionHelper.CreateScanExpression(parameter, property, ToScanOp(), new R[] { value });
            var predicateLambda = Expression.Lambda<Func<T, bool>>(expr, parameter);
            query = query.Where(predicateLambda);

            this.conditionMet = query.Any();

            return this;

            ScanOperator ToScanOp()
            {
                return condition switch
                {
                    PatchCondition.Equals => ScanOperator.Equal,
                    PatchCondition.GreaterThan => ScanOperator.GreaterThan,
                    PatchCondition.GreaterThanOrEqual => ScanOperator.GreaterThanOrEqual,
                    PatchCondition.LessThan => ScanOperator.LessThan,
                    PatchCondition.LessThanOrEqual => ScanOperator.LessThanOrEqual,
                    PatchCondition.NotEquals => ScanOperator.NotEqual,
                    PatchCondition.StartsWith => ScanOperator.BeginsWith,
                    _ => throw new InvalidOperationException()
                };
            }
        }

        public IPatchBuilder<T> IfContains<R>(Expression<Func<T, IEnumerable<R>>> property, R value)
        {
            var query = repo.Rows;

            var hashKeyPredicate = ExpressionHelper.CreateHashKeyPredicate<T>(hashKey);
            query = query.Where(hashKeyPredicate);
            if (rangeKey.HasValue)
            {
                var rangeKeyPredicate = ExpressionHelper.CreateRangeKeyKeyPredicate<T>(rangeKey.Value);
                query = query.Where(rangeKeyPredicate);
            }

            var row = query.SingleOrDefault();
            if (row is null)
            {
                conditionMet = false;
                return this;
            }

            var prop = ExpressionHelper.GetPropertyInfo(property);
            var collection = prop.GetValue(row) as ICollection<R>;
            conditionMet = collection.Contains(value);

            return this;
        }

        public IPatchBuilder<T> Remove<R>(Expression<Func<T, R>> property)
        {
            var prop = ExpressionHelper.GetPropertyInfo(property);
            prop.SetValue(row, default);
            return this;
        }

        public IPatchBuilder<T> Set<R>(Expression<Func<T, R>> property, R value)
        {
            ExpressionHelper.GetPropertyInfo(property).SetValue(row, value);
            return this;
        }

        public IPatchBuilder<T> With<R>(R value)
        {
            var originalValues = JsonSerializer.Serialize(row);
            var newValues = JsonSerializer.Serialize(value);
            var merged = JsonHelper.Merge(originalValues, newValues);
            this.row = JsonSerializer.Deserialize<T>(merged);
            return this;
        }

        public async ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken)
        {
            if (conditionMet)
            {
                await repo.SaveAsync(row, cancellationToken);
                return true;
            }
            else
                return false;
        }

        public async ValueTask<T> ExecuteAndGetAsync(CancellationToken cancellationToken)
        {
            if (conditionMet)
            {
                await repo.SaveAsync(row, cancellationToken);
                return row;
            }
            else
                return default;
        }

        /// <summary>
        /// In case of collection checking items count, in case of string string length
        /// </summary>
        /// <typeparam name="R"></typeparam>
        /// <param name="property"></param>
        /// <param name="condition"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public IPatchBuilder<T> IfSize<R>(Expression<Func<T, R>> property, PatchCondition condition, int value)
        {
            if (!conditionMet)
                return this;

            var query = repo.Rows;

            var hashKeyPredicate = ExpressionHelper.CreateHashKeyPredicate<T>(hashKey);
            query = query.Where(hashKeyPredicate);
            if (rangeKey.HasValue)
            {
                var rangeKeyPredicate = ExpressionHelper.CreateRangeKeyKeyPredicate<T>(rangeKey.Value);
                query = query.Where(rangeKeyPredicate);
            }

            var prop = ExpressionHelper.GetPropertyInfo(property);
            var propValue = prop.GetValue(row);
            if(propValue != null)
            {
                if (propValue.GetType().GetInterface(nameof(ICollection)) != null)
                {
                    conditionMet = IsConditionMet(((IEnumerable)propValue).Cast<object>().Count(), value);
                    return this;
                }
            }

            var stringValue = prop.GetValue(row) as string;

            if(stringValue != null)
            {
                conditionMet = IsConditionMet(stringValue.Length, value);
                return this;
            }

            return this;

            bool IsConditionMet(int sourceValue, int value)
            {
                return condition switch
                {
                    PatchCondition.Equals => sourceValue == value,
                    PatchCondition.GreaterThan => sourceValue > value,
                    PatchCondition.GreaterThanOrEqual => sourceValue >= value,
                    PatchCondition.LessThan => sourceValue < value,
                    PatchCondition.LessThanOrEqual => sourceValue <= value,
                    PatchCondition.NotEquals => sourceValue != value,
                    _ => throw new InvalidOperationException()
                };
            }
        }
    }
}
