using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CloudMesh;

/// <summary>
/// Reads and writes deeply nested object values addressed by a string "dot notation" path,
/// such as <c>"Address.City"</c> or <c>"Orders[0].Lines[\"sku-1\"].Quantity"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Access is performed through compiled expression-tree accessors (get/set delegates), not per-call
/// reflection, so repeated access to the same property is fast. Compiled paths, per-type property
/// accessors, and collection metadata are all cached in thread-safe static dictionaries and reused
/// for the lifetime of the process.
/// </para>
/// <para>
/// <b>Path syntax:</b>
/// </para>
/// <list type="bullet">
///   <item><description>Properties are separated by <c>.</c> — e.g. <c>"Customer.Name"</c>.</description></item>
///   <item><description>List / array elements use a numeric indexer — e.g. <c>"Orders[2]"</c>.</description></item>
///   <item><description>Dictionary entries use a bracket key, optionally quoted — e.g. <c>"Tags[\"env\"]"</c> or <c>"Scores[42]"</c>.</description></item>
///   <item><description>Indexers can be chained onto properties — e.g. <c>"Orders[0].Lines[1].Sku"</c>.</description></item>
/// </list>
/// <para>
/// Dictionary keys are converted from their string form to the dictionary's key type. Supported key
/// types are <see cref="string"/>, <see cref="int"/>, <see cref="long"/>, <see cref="Guid"/>, and enums.
/// Reading a missing property returns <see langword="null"/>; writing auto-creates intermediate objects,
/// dictionary entries, and list slots as needed (see <see cref="CompiledPath.SetValue"/>).
/// </para>
/// <example>
/// <code>
/// var order = new Order();
/// DotNotation.SetValue(order, "Customer.Name", "Ada");
/// DotNotation.SetValue(order, "Lines[0].Sku", "ABC");     // grows the list and creates Lines[0]
/// var name = (string?)DotNotation.GetValue(order, "Customer.Name");   // "Ada"
///
/// // Compile once, reuse many times:
/// var path = DotNotation.Compile("Customer.Name");
/// path.SetValue(order, "Grace");
/// </code>
/// </example>
/// </remarks>
public static class DotNotation
{
    private static readonly ConcurrentDictionary<string, CompiledPath> PathCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<MemberKey, Accessor> AccessorCache = new();
    private static readonly ConcurrentDictionary<Type, CollectionInfo> CollectionCache = new();

    /// <summary>
    /// Reads the value found at <paramref name="dotNotationPath"/> on <paramref name="instance"/>.
    /// </summary>
    /// <param name="instance">The root object to read from.</param>
    /// <param name="dotNotationPath">The dot-notation path to the value (see <see cref="DotNotation"/> for syntax).</param>
    /// <returns>
    /// The value at the path, or <see langword="null"/> if any object along the path is
    /// <see langword="null"/> or an indexer references a missing element.
    /// </returns>
    /// <remarks>The path is compiled and cached on first use; subsequent calls reuse the compiled accessor.</remarks>
    public static object? GetValue(object instance, string dotNotationPath)
        => Compile(dotNotationPath).GetValue(instance);

    /// <summary>
    /// Writes <paramref name="value"/> to the location addressed by <paramref name="dotNotationPath"/> on
    /// <paramref name="instance"/>, creating any missing intermediate objects, dictionary entries, or list
    /// slots along the way.
    /// </summary>
    /// <param name="instance">The root object to write to.</param>
    /// <param name="dotNotationPath">The dot-notation path to the target (see <see cref="DotNotation"/> for syntax).</param>
    /// <param name="value">The value to assign; must be assignable to the target member's type.</param>
    /// <remarks>The path is compiled and cached on first use; subsequent calls reuse the compiled accessor.</remarks>
    public static void SetValue(object instance, string dotNotationPath, object? value)
        => Compile(dotNotationPath).SetValue(instance, value);

    /// <summary>
    /// Parses and compiles a dot-notation path into a reusable <see cref="CompiledPath"/>.
    /// </summary>
    /// <param name="dotNotationPath">The dot-notation path to compile (see <see cref="DotNotation"/> for syntax).</param>
    /// <returns>A cached, reusable <see cref="CompiledPath"/> for the given path string.</returns>
    /// <remarks>
    /// Results are cached by path string, so calling this repeatedly with the same path returns the same
    /// instance. Prefer this over <see cref="GetValue"/>/<see cref="SetValue"/> when you access the same
    /// path against many instances in a tight loop.
    /// </remarks>
    public static CompiledPath Compile(string dotNotationPath)
        => PathCache.GetOrAdd(dotNotationPath, static path => new CompiledPath(Parse(path)));

    /// <summary>
    /// Converts a strongly typed member-access lambda into its equivalent dot-notation path string.
    /// </summary>
    /// <typeparam name="TSource">The root type the expression is rooted on.</typeparam>
    /// <typeparam name="TProp">The type of the member selected by the expression.</typeparam>
    /// <param name="pathExpression">
    /// A member-access expression such as <c>e =&gt; e.Address.City</c> or <c>e =&gt; e.Tags["env"]</c>.
    /// Captured variables used as indexers are evaluated and folded into the resulting path.
    /// </param>
    /// <returns>
    /// A tuple of the resulting dot-notation <c>PropertyPath</c> and the <c>MemberType</c> of the selected member.
    /// </returns>
    /// <example>
    /// <code>
    /// var (path, type) = DotNotation.ToDotNotation&lt;Order, string&gt;(o =&gt; o.Customer.Name);
    /// // path == "Customer.Name", type == typeof(string)
    /// </code>
    /// </example>
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

    /// <summary>
    /// A parsed, reusable dot-notation path that can read and write the addressed value on any compatible
    /// instance. Obtain one via <see cref="DotNotation.Compile"/>.
    /// </summary>
    /// <remarks>
    /// Instances are immutable and thread-safe; the underlying property accessors and collection metadata
    /// are compiled once and shared through <see cref="DotNotation"/>'s caches.
    /// </remarks>
    public sealed class CompiledPath
    {
        private readonly Segment[] segments;

        internal CompiledPath(Segment[] segments)
            => this.segments = segments;

        /// <summary>
        /// Reads the value addressed by this path on <paramref name="instance"/>.
        /// </summary>
        /// <param name="instance">The root object to read from.</param>
        /// <returns>
        /// The value at the path, or <see langword="null"/> if any object along the path is
        /// <see langword="null"/> or an indexer references a missing element.
        /// </returns>
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

        /// <summary>
        /// Writes <paramref name="value"/> to the location addressed by this path on
        /// <paramref name="instance"/>, creating any missing intermediate objects, dictionary entries, or
        /// list slots along the way.
        /// </summary>
        /// <param name="instance">The root object to write to.</param>
        /// <param name="value">The value to assign; must be assignable to the target member's type.</param>
        /// <remarks>
        /// Missing intermediate reference-typed properties are instantiated with their parameterless
        /// constructor. When a list indexer points past the end of the list, the list is grown (padding
        /// with default elements) so the slot exists. An empty path is a no-op.
        /// </remarks>
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