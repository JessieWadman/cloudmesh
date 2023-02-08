using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Expression = System.Linq.Expressions.Expression;

namespace CloudMesh.Persistence.DynamoDB.Helpers
{
    public static class ExpressionHelper
    {
        public static PropertyInfo GetPropertyInfo<TSource, TProperty>(
            Expression<Func<TSource, TProperty>> propertyLambda)
        {
            var type = typeof(TSource);

            if (propertyLambda.Body is not MemberExpression memberExpression || memberExpression == null)
            {
                throw new ArgumentException(string.Format(
                    "Expression '{0}' refers to a method, not a property.",
                    propertyLambda.ToString()));
            }

            var propInfo = memberExpression.Member as PropertyInfo 
                ?? throw new ArgumentException($"Expression '{propertyLambda}' refers to a field, not a property.");

            if (type != propInfo.ReflectedType && !type.IsSubclassOf(propInfo.ReflectedType!))
            {
                throw new ArgumentException($"Expression '{propertyLambda}' refers to a property that is not from type {type}.");
            }

            return propInfo;
        }

        public static Expression<Func<T, object>> ToLambda<T>(string propertyName)
        {
            var parameter = Expression.Parameter(typeof(T));
            var property = Expression.Property(parameter, propertyName);
            var propAsObject = Expression.Convert(property, typeof(object));

            return Expression.Lambda<Func<T, object>>(propAsObject, parameter);
        }

        private static object? ConvertToPropertyType(object value, Type propertyType)
        {
            if (value is null)
                return null;

            if (value is string guidString && propertyType == typeof(Guid))
                return Guid.Parse(guidString);
            else if (propertyType == typeof(string) && value.GetType() != typeof(string))
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            else if (value is string enumString && propertyType.IsEnum)
                return Enum.Parse(propertyType, enumString);
            return value;
        }

        public static Expression<Func<T, bool>> CreatePredicate<T>(params (PropertyInfo property, object value)[] predicates)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            Expression? comparison = null;
            foreach (var (property, value) in predicates)
            {
                var temp = ConvertToPropertyType(value, property.PropertyType);
                var current = Expression.Equal(Expression.Property(parameter, property.Name), Expression.Constant(temp));
                if (comparison is null)
                    comparison = current;
                else
                    comparison = Expression.And(comparison, current);
            }

            var predicateLambda = Expression.Lambda<Func<T, bool>>(comparison, parameter);

            return predicateLambda;
        }

        public static Expression<Func<T, bool>> CreatePredicate<T>(PropertyInfo property, object value)
            => CreatePredicate<T>((property, value));

        private static MemberExpression? ExtractMemberExpression(Expression expression)
        {
            if (expression.NodeType == ExpressionType.MemberAccess)
            {
                return (MemberExpression)expression;
            }

            if (expression.NodeType == ExpressionType.Convert)
            {
                var operand = ((UnaryExpression)expression).Operand;
                return ExtractMemberExpression(operand);
            }

            return null;
        }

        public static DynamoDBPropertyAttribute? GetDynamoDBAttribute(PropertyInfo property)
            => (from a in property.GetCustomAttributes(true).OfType<DynamoDBPropertyAttribute>()
                let t = a.GetType()
                where typeof(DynamoDBPropertyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBHashKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBRangeKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBGlobalSecondaryIndexHashKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBGlobalSecondaryIndexRangeKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBLocalSecondaryIndexRangeKeyAttribute).IsAssignableFrom(t)
                select a
                ).FirstOrDefault();

        public static IEnumerable<DynamoDBPropertyAttribute> GetDynamoDBAttributes(PropertyInfo property)
            => (from a in property.GetCustomAttributes(true).OfType<DynamoDBPropertyAttribute>()
                let t = a.GetType()
                where typeof(DynamoDBPropertyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBHashKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBRangeKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBGlobalSecondaryIndexHashKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBGlobalSecondaryIndexRangeKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBLocalSecondaryIndexRangeKeyAttribute).IsAssignableFrom(t)
                select a
                ).ToArray();

        public static string GetDynamoDBPropertyName(PropertyInfo property)
            => (from a in property.GetCustomAttributes(true).OfType<DynamoDBPropertyAttribute>()
                let t = a.GetType()
                where typeof(DynamoDBPropertyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBHashKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBRangeKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBGlobalSecondaryIndexHashKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBGlobalSecondaryIndexRangeKeyAttribute).IsAssignableFrom(t) &&
                      !typeof(DynamoDBLocalSecondaryIndexRangeKeyAttribute).IsAssignableFrom(t)
                select a.AttributeName
                ).FirstOrDefault() ?? property.Name;

        public static string GetDynamoDBPropertyName<T, R>(Expression<Func<T, R>> selector)
            => GetDynamoDBPropertyName(GetPropertyInfo(selector));

        public static (string PropertyPath, Type MemberType) GetDotNotation<T, R>(Expression<Func<T, R>> pathExpression)
        {
            pathExpression = (Expression<Func<T, R>>)new Visitor().Visit(pathExpression);

            var str = pathExpression.Body.ToString();
            str = str[(str.IndexOf(".") + 1)..];
            str = str.Replace(".get_Item(\"", "[\"");
            str = str.Replace("\")", "\"]");
            str = str.Replace(".get_Item(", "[");
            str = str.Replace(")", "]");

            return (str, pathExpression.ReturnType);
        }

        public static string DotNotationToDynamoDBExpression<T>(string propertyPath)
        {
            var result = string.Empty;
            var parts = propertyPath.Split('.');
            var currentType = typeof(T);
            foreach (var part in parts)
            {
                var temp = part;
                var arrayNotationIdx = part.IndexOf('[');
                if (arrayNotationIdx > 0)
                    temp = part.Substring(0, arrayNotationIdx);
                var property = currentType.GetProperty(temp, BindingFlags.Instance | BindingFlags.Public) 
                    ?? throw new InvalidOperationException($"The property {temp} does not exist on type {currentType.Name}");
                var dynamoDBPropertyName = GetDynamoDBPropertyName(property);
                if (result != string.Empty)
                    result += ".";
                result += dynamoDBPropertyName;

                if (arrayNotationIdx > 0)
                {
                    var arrayPart = part[temp.Length..];
                    result += arrayPart;
                    currentType = property.PropertyType.GenericTypeArguments.Last();
                }
                else
                    currentType = property.PropertyType;
            }
            return result;
        }

        public static PropertyInfo? TryGetHashKeyProperty<T>()
        {
            var propQuery = from prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            let attrib = prop.GetCustomAttributes(true).OfType<DynamoDBHashKeyAttribute>().FirstOrDefault()
                            where attrib is not null && attrib.GetType() == typeof(DynamoDBHashKeyAttribute)
                            select prop;

            return propQuery.FirstOrDefault();
        }

        public static PropertyInfo GetHashKeyProperty<T>()
            => TryGetHashKeyProperty<T>() ?? throw new InvalidOperationException($"Missing DynamoDBHashKey attribute on type {typeof(T).Name}");
        

        public static PropertyInfo? TryGetRangeKeyProperty<T>()
        {
            var propQuery = from prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            let attrib = prop.GetCustomAttributes(true).OfType<DynamoDBRangeKeyAttribute>().FirstOrDefault()
                            where attrib is not null && attrib.GetType() == typeof(DynamoDBRangeKeyAttribute)
                            select prop;

            return propQuery.FirstOrDefault();
        }

        public static PropertyInfo GetRangeKeyProperty<T>()
            => TryGetRangeKeyProperty<T>() ?? throw new InvalidOperationException($"Missing DynamoDBRangeKey attribute on type {typeof(T).Name}");


        public static bool HasRangeKeyProperty<T>()
            => TryGetRangeKeyProperty<T>() != null;
        

        public static PropertyInfo GetGlobalSecondaryHashKeyProperty<T>(string indexName)
        {
            var propQuery = from prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            let attrib = prop.GetCustomAttributes(true).OfType<DynamoDBGlobalSecondaryIndexHashKeyAttribute>().FirstOrDefault()
                            where attrib is not null && attrib.IndexNames.Contains(indexName)
                            select prop;

            return propQuery.Single();
        }

        public static PropertyInfo GetGlobalSecondaryIndexRangeKeyProperty<T>(string indexName)
        {
            var propQuery = from prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            let attrib = prop.GetCustomAttributes(true).OfType<DynamoDBGlobalSecondaryIndexRangeKeyAttribute>().FirstOrDefault()
                            where attrib is not null && attrib.IndexNames.Contains(indexName)
                            where attrib is not null
                            select prop;

            return propQuery.Single();
        }

        public static Expression<Func<T, bool>> CreateHashKeyPredicate<T>(DynamoDBValue value)
            => CreatePredicate<T>(GetHashKeyProperty<T>(), value.ToObject());

        public static Expression<Func<T, bool>> CreateRangeKeyKeyPredicate<T>(DynamoDBValue value)
            => CreatePredicate<T>(GetRangeKeyProperty<T>(), value.ToObject());

        public static Expression<Func<T, bool>> CreateGlobalSecondaryHashKeyPredicate<T>(string indexName, DynamoDBValue value)
            => CreatePredicate<T>(GetGlobalSecondaryHashKeyProperty<T>(indexName), value.Value);

        public static Expression<Func<T, bool>> CreateGlobalSecondaryIndexRangeKeyKeyPredicate<T>(string indexName, DynamoDBValue value)
            => CreatePredicate<T>(GetGlobalSecondaryIndexRangeKeyProperty<T>(indexName), value.Value);

        private static Expression ToStrings(Expression left, Expression right, Func<Expression, Expression, Expression> op)
        {
            return op(Expression.Call(left, "ToString", Type.EmptyTypes), Expression.Call(right, "ToString", Type.EmptyTypes));
        }

        private static Expression Comparison(
            PropertyInfo prop,
            Expression propExpression,
            QueryOperator op,
            object value)
        {
            if (prop.PropertyType == typeof(string))
            {
                var left = Expression.Call(propExpression, "ToString", Type.EmptyTypes);
                Expression right;
                if (value is string str)
                    right = Expression.Constant(str);
                else
                    right = Expression.Call(Expression.Constant(value), "ToString", Type.EmptyTypes);

                var method = typeof(string).GetMethod("Compare", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(string), typeof(StringComparison) }, null);
                var comparison = Expression.Call(null, method, left, right, Expression.Constant(StringComparison.InvariantCulture));
                return op switch
                {
                    QueryOperator.GreaterThan
                        => Expression.GreaterThan(comparison, Expression.Constant(0)),
                    QueryOperator.GreaterThanOrEqual
                        => Expression.GreaterThanOrEqual(comparison, Expression.Constant(0)),
                    QueryOperator.LessThan
                        => Expression.LessThan(comparison, Expression.Constant(0)),
                    QueryOperator.LessThanOrEqual
                        => Expression.LessThanOrEqual(comparison, Expression.Constant(0)),
                    _ => throw new NotSupportedException()
                };
            }
            else return op switch
            {
                QueryOperator.GreaterThan
                    => Expression.GreaterThan(propExpression, Expression.Constant(value)),
                QueryOperator.GreaterThanOrEqual
                    => Expression.GreaterThanOrEqual(propExpression, Expression.Constant(value)),
                QueryOperator.LessThan
                    => Expression.LessThan(propExpression, Expression.Constant(value)),
                QueryOperator.LessThanOrEqual
                    => Expression.LessThanOrEqual(propExpression, Expression.Constant(value)),
                _ => throw new NotSupportedException()
            };
        }

        private static Expression Comparison(
            PropertyInfo prop,
            Expression propExpression,
            ScanOperator op,
            object value)
        {
            if (prop.PropertyType == typeof(string))
            {
                var left = Expression.Call(propExpression, "ToString", Type.EmptyTypes);
                Expression right;
                if (value is string str)
                    right = Expression.Constant(str);
                else
                    right = Expression.Call(Expression.Constant(value), "ToString", Type.EmptyTypes);

                var method = typeof(string).GetMethod("Compare", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(string), typeof(StringComparison) }, null);
                var comparison = Expression.Call(null, method, left, right, Expression.Constant(StringComparison.InvariantCulture));
                return op switch
                {
                    ScanOperator.GreaterThan
                        => Expression.GreaterThan(comparison, Expression.Constant(0)),
                    ScanOperator.GreaterThanOrEqual
                        => Expression.GreaterThanOrEqual(comparison, Expression.Constant(0)),
                    ScanOperator.LessThan
                        => Expression.LessThan(comparison, Expression.Constant(0)),
                    ScanOperator.LessThanOrEqual
                        => Expression.LessThanOrEqual(comparison, Expression.Constant(0)),
                    _ => throw new NotSupportedException()
                };
            }
            else return op switch
            {
                ScanOperator.GreaterThan
                    => Expression.GreaterThan(propExpression, Expression.Constant(value)),
                ScanOperator.GreaterThanOrEqual
                    => Expression.GreaterThanOrEqual(propExpression, Expression.Constant(value)),
                ScanOperator.LessThan
                    => Expression.LessThan(propExpression, Expression.Constant(value)),
                ScanOperator.LessThanOrEqual
                    => Expression.LessThanOrEqual(propExpression, Expression.Constant(value)),
                _ => throw new NotSupportedException()
            };
        }

        public static Expression<Func<T, bool>> CreatePredicate<T>(
            string indexName,
            DynamoDBValue hashKey,
            (QueryOperator Operator, DynamoDBValue[] Values)? rangeKeys)
        {
            PropertyInfo hashKeyProp;
            if (indexName is null)
                hashKeyProp = GetHashKeyProperty<T>();
            else
                hashKeyProp = GetGlobalSecondaryHashKeyProperty<T>(indexName);

            var parameter = Expression.Parameter(typeof(T), "x");

            BinaryExpression comparison = null;
            
            if (hashKey.ValueType == DynamoDBValueType.String)
            {
                var propExpr = Expression.Property(parameter, hashKeyProp.Name);

                // We are comparing a string against a value type, use ToString on the value type
                // ((object)Dto.Prop).ToString() == hashKey.ToObject().ToString()
                if (propExpr.Type.IsValueType)
                {
                    comparison = Expression.Equal(
                        Expression.Call(
                            Expression.Convert(propExpr, typeof(object)), typeof(object).GetMethod("ToString")),
                        Expression.Constant(hashKey.ToObject().ToString()));
                }
                // It's not a value type, so we need to do a ?.ToString() 
                else
                {
                    // x.EmployeeNo == null ? (string)null : x.EmployeeNo.ToString()
                    var nullableToString = Expression.Condition(
                        Expression.Equal(propExpr, Expression.Default(propExpr.Type)),
                        Expression.Constant(null, typeof(string)),
                        Expression.Call(Expression.Convert(propExpr, typeof(object)), typeof(object).GetMethod("ToString")));

                    // x => (x.EmployeeNo == null ? (string)null : x.EmployeeNo.ToString()) == '142'
                    comparison = Expression.Equal(nullableToString, Expression.Constant(hashKey.ToObject().ToString()));
                }
            }
            else
            {
                // We're not comparing strings
                comparison = Expression.Equal(Expression.Property(parameter, hashKeyProp.Name), Expression.Constant(hashKey.ToObject()));
            }

            if (rangeKeys.HasValue)
            {
                PropertyInfo rangeKeyProp;
                if (indexName is null)
                    rangeKeyProp = GetRangeKeyProperty<T>();
                else
                    rangeKeyProp = GetGlobalSecondaryIndexRangeKeyProperty<T>(indexName);

                var rangeKeyPropExpression = Expression.Property(parameter, rangeKeyProp.Name);

                var rangePropComparison = rangeKeys.Value.Operator switch
                {
                    QueryOperator.BeginsWith
                        => ToStrings(rangeKeyPropExpression, Expression.Constant(rangeKeys.Value.Values[0].Value),
                        (l, r) => Expression.Call(l, "StartsWith", Type.EmptyTypes, r, Expression.Constant(StringComparison.InvariantCulture))),
                    QueryOperator.Between
                        => Expression.And(
                                Comparison(rangeKeyProp, rangeKeyPropExpression, QueryOperator.GreaterThanOrEqual, rangeKeys.Value.Values[0].ToObject()),
                                Comparison(rangeKeyProp, rangeKeyPropExpression, QueryOperator.LessThanOrEqual, rangeKeys.Value.Values[1].ToObject())
                            ),
                    QueryOperator.Equal
                        => Expression.Equal(rangeKeyPropExpression, Expression.Constant(rangeKeys.Value.Values[0].ToObject())),
                    _ => Comparison(rangeKeyProp, rangeKeyPropExpression, rangeKeys.Value.Operator, rangeKeys.Value.Values[0].ToObject())
                };

                comparison = Expression.And(comparison, rangePropComparison);
            }

            var predicateLambda = Expression.Lambda<Func<T, bool>>(comparison, parameter);

            return predicateLambda;
        }

        private static Expression CreateEnumerableContainsExpression<R>(PropertyInfo property, MemberExpression parameter, R[] values)
        {
            var method = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ToString() == "Boolean Contains[TSource](System.Collections.Generic.IEnumerable`1[TSource], TSource)")
                .Single();
            method = method.MakeGenericMethod(property.PropertyType);
            return Expression.Call(null, method, Expression.Constant(values), parameter);
        }

        public static Expression CreateScanExpression<T, R>(
            ParameterExpression parameter,
            Expression<Func<T, R>> selector, ScanOperator op, R[] values)
        {
            var property = GetPropertyInfo(selector);
            var propertyExpression = Expression.Property(parameter, property.Name);

            return op switch
            {
                ScanOperator.BeginsWith => ToStrings(propertyExpression, Expression.Constant(values[0]),
                                                    (l, r) => Expression.Call(l, "StartsWith", Type.EmptyTypes, r, Expression.Constant(StringComparison.InvariantCulture))),
                ScanOperator.Between => Expression.And(
                                            Comparison(property, propertyExpression, ScanOperator.GreaterThanOrEqual, values[0]),
                                            Comparison(property, propertyExpression, ScanOperator.LessThanOrEqual, values[1])
                                        ),
                ScanOperator.Equal => Expression.Equal(propertyExpression, Expression.Constant(values[0])),
                ScanOperator.IsNotNull => Expression.NotEqual(propertyExpression, Expression.Constant(null)),
                ScanOperator.IsNull => Expression.Equal(propertyExpression, Expression.Constant(null)),
                ScanOperator.NotEqual => Expression.NotEqual(propertyExpression, Expression.Constant(values[0])),
                ScanOperator.Contains => Expression.Call(
                    Expression.Call(propertyExpression, "ToString", Type.EmptyTypes),
                    "Contains", Type.EmptyTypes, Expression.Constant(values[0].ToString()), Expression.Constant(StringComparison.InvariantCulture)),
                ScanOperator.NotContains => Expression.Not(Expression.Call(
                    Expression.Call(propertyExpression, "ToString", Type.EmptyTypes),
                    "Contains", Type.EmptyTypes, Expression.Constant(values[0].ToString()), Expression.Constant(StringComparison.InvariantCulture))),
                ScanOperator.In => CreateEnumerableContainsExpression(property, propertyExpression, values),
                _ => Comparison(property, propertyExpression, op, values[0])
            };
        }

        public static Expression CreateScanExpression<T, R>(
            ParameterExpression parameter,
            Expression<Func<T, IEnumerable<R>>> selector, ScanOperator op, R value)
        {
            var property = GetPropertyInfo(selector);
            var propertyExpression = Expression.Property(parameter, property.Name);

            var methodInfo = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
                .Single();

            methodInfo = methodInfo.MakeGenericMethod(new Type[] { typeof(R) });

            Expression comparison = Expression.Call(methodInfo, propertyExpression, Expression.Constant(value));
            if (op == ScanOperator.NotContains)
                comparison = Expression.Not(comparison);

            return comparison;
        }

        class Visitor : ExpressionVisitor
        {
            protected override Expression VisitMember
                (MemberExpression memberExpression)
            {
                // Recurse down to see if we can simplify...
                var expression = Visit(memberExpression.Expression);

                // If we've ended up with a constant, and it's a property or a field,
                // we can simplify ourselves to a constant
                if (expression is ConstantExpression constantExpression)
                {
                    var container = constantExpression.Value;
                    var member = memberExpression.Member;
                    if (member is FieldInfo fi)
                    {
                        var value = fi.GetValue(container);
                        return Expression.Constant(value, fi.FieldType);
                    }
                    if (member is PropertyInfo propertyInfo)
                    {
                        var value = propertyInfo.GetValue(container, null);
                        return Expression.Constant(value);
                    }
                }
                return base.VisitMember(memberExpression);
            }
        }
    }
}
