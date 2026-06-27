using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CloudMesh;

public static class DotNotation
{
    private static readonly ConcurrentDictionary<string, CompiledPath> PathCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<MemberKey, Accessor> AccessorCache = new();
    private static readonly ConcurrentDictionary<Type, CollectionInfo> CollectionCache = new();

    public static object? GetValue(object instance, string dotNotationPath)
        => Compile(dotNotationPath).GetValue(instance);

    public static void SetValue(object instance, string dotNotationPath, object? value)
        => Compile(dotNotationPath).SetValue(instance, value);

    // Additive API. Existing callers remain untouched.
    public static CompiledPath Compile(string dotNotationPath)
        => PathCache.GetOrAdd(dotNotationPath, static path => new CompiledPath(Parse(path)));

    public static (string PropertyPath, Type MemberType) ToDotNotation<TSource, TProp>(
        Expression<Func<TSource, TProp>> pathExpression)
    {
        pathExpression = (Expression<Func<TSource, TProp>>)new DotNotationVisitor().Visit(pathExpression)!;

        var str = pathExpression.Body.ToString();
        str = str[(str.IndexOf('.') + 1)..];

        str = str.Replace(".get_Item(\"", "[\"");
        str = str.Replace("\")", "\"]");
        str = str.Replace(".get_Item(", "[");
        str = str.Replace(")", "]");

        return (str, pathExpression.ReturnType);
    }

    private static Segment[] Parse(string path)
    {
        var result = new List<Segment>(4);
        var start = 0;
        var insideQuotes = false;
        var insideIndexer = false;

        for (var i = 0; i < path.Length; i++)
        {
            switch (path[i])
            {
                case '"':
                    insideQuotes = !insideQuotes;
                    break;

                case '[':
                    if (!insideIndexer && i > start)
                        AddProperty(path.AsSpan(start, i - start), result);

                    insideIndexer = true;
                    start = i + 1;
                    break;

                case ']':
                    if (insideIndexer)
                    {
                        AddIndexer(path.AsSpan(start, i - start), result);
                        insideIndexer = false;
                        start = i + 1;
                    }
                    break;

                case '.' when !insideQuotes && !insideIndexer:
                    if (i > start)
                        AddProperty(path.AsSpan(start, i - start), result);

                    start = i + 1;
                    break;
            }
        }

        if (start < path.Length)
            AddProperty(path.AsSpan(start), result);

        return result.ToArray();

        static void AddProperty(ReadOnlySpan<char> raw, List<Segment> result)
        {
            if (raw.Length != 0)
                result.Add(new Segment(raw.ToString(), null));
        }

        static void AddIndexer(ReadOnlySpan<char> raw, List<Segment> result)
        {
            if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                raw = raw[1..^1];

            result.Add(new Segment(string.Empty, raw.ToString()));
        }
    }

    public sealed class CompiledPath
    {
        private readonly Segment[] segments;

        internal CompiledPath(Segment[] segments)
            => this.segments = segments;

        public object? GetValue(object instance)
        {
            object? current = instance;

            foreach (var segment in segments)
            {
                if (current is null)
                    return null;

                if (segment.PropertyName.Length != 0)
                    current = GetAccessor(current.GetType(), segment.PropertyName).Getter(current);

                if (current is null)
                    return null;

                if (segment.Key is not null)
                    current = ReadIndex(current, segment.Key);
            }

            return current;
        }

        public void SetValue(object instance, object? value)
        {
            if (segments.Length == 0)
                return;

            var current = instance;

            for (var i = 0; i < segments.Length - 1; i++)
                current = ResolveForWrite(current, segments[i]);

            WriteFinal(current, segments[^1], value);
        }

        private static object ResolveForWrite(object instance, Segment segment)
        {
            object current = instance;

            if (segment.PropertyName.Length != 0)
                current = GetOrCreateProperty(current, segment.PropertyName);

            if (segment.Key is not null)
                current = GetOrCreateIndex(current, segment.Key);

            return current;
        }

        private static void WriteFinal(object instance, Segment segment, object? value)
        {
            if (segment.Key is null)
            {
                GetAccessor(instance.GetType(), segment.PropertyName).Setter(instance, value);
                return;
            }

            var target = segment.PropertyName.Length == 0
                ? instance
                : GetOrCreateProperty(instance, segment.PropertyName);

            WriteIndex(target, segment.Key, value);
        }

        private static object GetOrCreateProperty(object instance, string propertyName)
        {
            var accessor = GetAccessor(instance.GetType(), propertyName);
            var value = accessor.Getter(instance);

            if (value is not null)
                return value;

            value = CreateInstance(accessor.Type);
            accessor.Setter(instance, value);
            return value;
        }

        private static object GetOrCreateIndex(object instance, string key)
        {
            var info = GetCollectionInfo(instance.GetType());

            if (info.Kind == CollectionKind.Dictionary)
            {
                var dictionary = (IDictionary)instance;
                var convertedKey = ConvertKey(key, info.KeyType!);

                if (dictionary.Contains(convertedKey))
                {
                    var existing = dictionary[convertedKey];
                    if (existing is not null)
                        return existing;
                }

                var created = CreateInstance(info.ValueType!);
                dictionary[convertedKey] = created;
                return created;
            }

            if (info.Kind == CollectionKind.List)
            {
                var list = (IList)instance;
                var index = ParseIndex(key);

                while (list.Count <= index)
                    list.Add(GetDefaultValue(info.ElementType!));

                var existing = list[index];

                if (existing is not null)
                    return existing;

                var created = CreateInstance(info.ElementType!);
                list[index] = created;
                return created;
            }

            throw NotIndexable(instance);
        }

        private static object? ReadIndex(object instance, string key)
        {
            var info = GetCollectionInfo(instance.GetType());

            if (info.Kind == CollectionKind.Dictionary)
            {
                var dictionary = (IDictionary)instance;
                var convertedKey = ConvertKey(key, info.KeyType!);

                return dictionary.Contains(convertedKey)
                    ? dictionary[convertedKey]
                    : null;
            }

            if (info.Kind == CollectionKind.List)
            {
                var list = (IList)instance;
                var index = ParseIndex(key);

                return (uint)index < (uint)list.Count
                    ? list[index]
                    : null;
            }

            throw NotIndexable(instance);
        }

        private static void WriteIndex(object instance, string key, object? value)
        {
            var info = GetCollectionInfo(instance.GetType());

            if (info.Kind == CollectionKind.Dictionary)
            {
                ((IDictionary)instance)[ConvertKey(key, info.KeyType!)] = value;
                return;
            }

            if (info.Kind == CollectionKind.List)
            {
                var list = (IList)instance;
                var index = ParseIndex(key);

                while (list.Count <= index)
                    list.Add(GetDefaultValue(info.ElementType!));

                list[index] = value;
                return;
            }

            throw NotIndexable(instance);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Accessor GetAccessor(Type type, string propertyName)
        => AccessorCache.GetOrAdd(new MemberKey(type, propertyName), static key => CreateAccessor(key.Type, key.Name));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CollectionInfo GetCollectionInfo(Type type)
        => CollectionCache.GetOrAdd(type, static type =>
        {
            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                var dictionaryType = FindGenericInterface(type, typeof(IDictionary<,>)) ?? type;
                var args = dictionaryType.GetGenericArguments();

                if (args.Length == 2)
                    return new CollectionInfo(CollectionKind.Dictionary, args[0], args[1], null);
            }

            if (typeof(IList).IsAssignableFrom(type))
            {
                if (type.IsArray)
                    return new CollectionInfo(CollectionKind.List, null, null, type.GetElementType());

                var listType = FindGenericInterface(type, typeof(IList<>)) ?? type;
                var args = listType.GetGenericArguments();

                if (args.Length == 1)
                    return new CollectionInfo(CollectionKind.List, null, null, args[0]);
            }

            return default;
        });

    private static Type? FindGenericInterface(Type type, Type genericInterface)
    {
        if (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == genericInterface)
            return type;

        foreach (var candidate in type.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericInterface)
                return candidate;
        }

        return null;
    }

    private static Accessor CreateAccessor(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on type '{type.FullName}'.");

        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(object), "value");

        var typedInstance = Expression.Convert(instance, type);
        var propertyAccess = Expression.Property(typedInstance, property);

        var getter = Expression.Lambda<Func<object, object?>>(
            Expression.Convert(propertyAccess, typeof(object)),
            instance
        ).Compile();

        var setter = Expression.Lambda<Action<object, object?>>(
            Expression.Assign(
                propertyAccess,
                Expression.Convert(value, property.PropertyType)
            ),
            instance,
            value
        ).Compile();

        return new Accessor(getter, setter, property.PropertyType);
    }

    private static object CreateInstance(Type type)
        => Activator.CreateInstance(type)
           ?? throw new InvalidOperationException($"Could not create instance of '{type.FullName}'.");

    private static object? GetDefaultValue(Type type)
        => type.IsValueType ? Activator.CreateInstance(type) : null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ParseIndex(string key)
        => int.Parse(key, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static object ConvertKey(string key, Type targetType)
    {
        if (targetType == typeof(string)) return key;
        if (targetType == typeof(int)) return ParseIndex(key);
        if (targetType == typeof(long)) return long.Parse(key, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (targetType == typeof(Guid)) return Guid.Parse(key);
        if (targetType.IsEnum) return Enum.Parse(targetType, key);

        throw new InvalidOperationException($"Unsupported key type '{targetType.FullName}'.");
    }

    private static InvalidOperationException NotIndexable(object instance)
        => new($"Type '{instance.GetType().FullName}' is not indexable.");

    internal readonly record struct Segment(string PropertyName, string? Key);
    internal readonly record struct MemberKey(Type Type, string Name);

    internal sealed record Accessor(
        Func<object, object?> Getter,
        Action<object, object?> Setter,
        Type Type
    );

    internal readonly record struct CollectionInfo(
        CollectionKind Kind,
        Type? KeyType,
        Type? ValueType,
        Type? ElementType
    );

    internal enum CollectionKind
    {
        None,
        Dictionary,
        List
    }

    internal sealed class DotNotationVisitor : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression memberExpression)
        {
            var expression = Visit(memberExpression.Expression);

            if (expression is not ConstantExpression constant)
                return base.VisitMember(memberExpression);

            var container = constant.Value;

            return memberExpression.Member switch
            {
                FieldInfo field => Expression.Constant(field.GetValue(container), field.FieldType),
                PropertyInfo property => Expression.Constant(property.GetValue(container), property.PropertyType),
                _ => base.VisitMember(memberExpression)
            };
        }
    }
}