using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace CloudMesh;

/// <summary>
/// Dot notation helper to get and set nested properties using a dot notation path.
/// </summary>
public static class DotNotation
{
    private static readonly ConcurrentDictionary<string, Func<object, object?>> PropertyGettersCache = new();
    private static readonly ConcurrentDictionary<string, Action<object, object?>> PropertySettersCache = new();

    public static (string PropertyPath, Type MemberType) ToDotNotation<TSource, TProp>(
        Expression<Func<TSource, TProp>> pathExpression)
    {
        pathExpression = (Expression<Func<TSource, TProp>>)new DotNotationVisitor().Visit(pathExpression);

        var str = pathExpression.Body.ToString();
        str = str[(str.IndexOf('.') + 1)..];
        str = str.Replace(".get_Item(\"", "[\"");
        str = str.Replace("\")", "\"]");
        str = str.Replace(".get_Item(", "[");
        str = str.Replace(")", "]");

        return (str, pathExpression.ReturnType);
    }
    
    private static string[] ParseDotNotation(scoped ReadOnlySpan<char> dotNotation)
    {
        var result = new List<string>();
        var buffer = new StringBuilder();
        var insideQuotes = false;
        var insideIndexer = false;

        foreach (var currentChar in dotNotation)
        {
            switch (currentChar)
            {
                // Handle opening/closing double quotes
                case '\"':
                    insideQuotes = !insideQuotes;
                    buffer.Append(currentChar); // Keep the quotes in the key to identify dictionary keys with dots
                    continue;
                // Handle opening/closing indexer brackets
                case '[':
                {
                    insideIndexer = true;
                    if (buffer.Length > 0)
                    {
                        result.Add(buffer.ToString());
                        buffer.Clear();
                    }
                    buffer.Append(currentChar); // Start capturing inside the indexer
                    continue;
                }
                case ']':
                    insideIndexer = false;
                    buffer.Append(currentChar);
                    result.Add(buffer.ToString());
                    buffer.Clear();
                    continue;
                // Handle dots (property separators) outside of quotes and indexers
                case '.' when !insideQuotes && !insideIndexer:
                {
                    if (buffer.Length > 0)
                    {
                        result.Add(buffer.ToString());
                        buffer.Clear();
                    }
                    continue;
                }
                default:
                    // Add the current character to the buffer
                    buffer.Append(currentChar);
                    break;
            }
        }

        // Add any remaining characters in the buffer as the last part
        if (buffer.Length > 0)
        {
            result.Add(buffer.ToString());
        }

        return result.ToArray();
    }

    /// <summary>
    /// Get the value of the specified nested property value from the given instance. 
    /// </summary>
    /// <param name="instance">Root object whose property is the first part of the dot notation.</param>
    /// <param name="dotNotationPath">A dot notation path to the property to read</param>
    /// <returns>The value, if possible</returns>
    /// <example>
    /// <code>
    ///     var post = new Post();
    ///     var value = DotNotation.GetValue(obj, "Authors[0].Attributes[\"ContactDetails\"].Email");
    /// </code>
    /// </example>
    public static object? GetValue(object instance, string dotNotationPath)
    {
        var parts = ParseDotNotation(dotNotationPath);
        return GetValueRecursive(instance, parts, 0);
    }

    /// <summary>
    /// Sets the value of the specified nested property value from the given instance.
    /// </summary>
    /// <param name="instance">Root object whose property is the first part of the dot notation.</param>
    /// <param name="dotNotationPath">A dot notation path to the property to write</param>
    /// <param name="value">The value to write</param>
    /// <example>
    /// <code>
    ///     var post = new Post();
    ///     var value = DotNotation.SetValue(obj, "Authors[0].Attributes[\"ContactDetails\"].Email", "bob@domain.com");
    /// </code>
    /// </example>
    public static void SetValue(object instance, string dotNotationPath, object? value)
    {
        var parts = ParseDotNotation(dotNotationPath);
        SetValueRecursive(instance, parts, 0, value, null);
    }

    private static object? GetValueRecursive(object instance, string[] parts, int index)
    {
        if (index >= parts.Length) 
            return instance;

        var part = parts[index];
        if (IsIndexedPart(part, out var propertyName, out var keyPart))
        {
            var currentValue = GoToProperty(instance, propertyName, out var currentType);

            if (currentValue == null) 
                return null;

            if (typeof(IDictionary).IsAssignableFrom(currentType))
            {
                var dictionary = (IDictionary)currentValue;
                var keyType = currentType.GetGenericArguments()[0]; // Get dictionary key type
                var keyObject = ConvertKeyType(keyPart, keyType);

                return dictionary.Contains(keyObject) 
                    ? GetValueRecursive(dictionary[keyObject]!, parts, index + 1) 
                    : null;
            }

            if (!typeof(IList).IsAssignableFrom(currentType)) 
                return null; // Invalid access type
            
            var list = (IList)currentValue;
            var indexValue = (int)ConvertKeyType(keyPart, typeof(int));

            return indexValue < list.Count 
                ? GetValueRecursive(list[indexValue]!, parts, index + 1) 
                : null; // Index out of range
        }

        var getter = PropertyGettersCache.GetOrAdd(
            $"{instance.GetType().FullName}.{part}", 
            _ => CreatePropertyGetter(instance.GetType(), part));
        
        var nextInstance = getter(instance);
        return nextInstance == null ? null : GetValueRecursive(nextInstance, parts, index + 1);
    }

    private static object? GoToProperty(object instance, string propertyName, out Type? propertyType)
    {
        object? propertyValue;
            
        if (propertyName == string.Empty)
        {
            propertyValue = instance;
            propertyType = instance.GetType();
        }
        else
        {
            var propertyInfo = instance.GetType().GetProperty(propertyName);
            if (propertyInfo == null)
            {
                throw new InvalidOperationException(
                    $"Property '{propertyName}' not found on type {instance.GetType().FullName}.");
            }

            propertyValue = propertyInfo.GetValue(instance);
            propertyType = propertyInfo.PropertyType;
        }

        return propertyValue;
    }

    private static object? GetDefaultValue(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static void SetValueRecursive(object instance, string[] parts, int index, object? value, object? key)
    {
        if (index >= parts.Length - 1)
        {
            var part = parts[index];

            if (key == null && IsIndexedPart(part, out var propertyName, out var keyPart))
            {
                var propertyType = instance.GetType();
                var propertyValue = instance;
                
                if (propertyName != string.Empty)
                {
                    var propertyInfo = instance.GetType().GetProperty(propertyName)
                        ?? throw new InvalidOperationException($"Property '{propertyName}' not found on type {instance.GetType().FullName}.");
                    propertyType = propertyInfo.PropertyType;
                    propertyValue = propertyInfo.GetValue(instance) ?? Activator.CreateInstance(propertyInfo.PropertyType);
                    propertyInfo.SetValue(instance, propertyValue);
                }

                if (typeof(IDictionary).IsAssignableFrom(propertyType))
                {
                    var dictionary = (IDictionary)propertyValue!;
                    var keyType = propertyType.GetGenericArguments()[0];
                    var keyObject = ConvertKeyType(keyPart, keyType);
                    dictionary[keyObject] = value;
                }
                else if (typeof(IList).IsAssignableFrom(propertyType))
                {
                    var list = (IList)propertyValue!;
                    var indexValue = (int)ConvertKeyType(keyPart, typeof(int));
                    while (list.Count <= indexValue)
                    {
                        list.Add(GetDefaultValue(propertyType.GetGenericArguments()[0])!);
                    }
                    list[indexValue] = value;
                }
            }
            else
            {
                var setter = PropertySettersCache.GetOrAdd($"{instance.GetType().FullName}.{parts[index]}", path => CreatePropertySetter(instance.GetType(), parts[index]));
                setter(instance, value);
            }
            return;
        }

        var nextPart = parts[index];
        if (IsIndexedPart(nextPart, out var propertyNameNext, out var keyPartNext))
        {
            if (!string.IsNullOrEmpty(propertyNameNext))
            {
                var propertyInfoNext = instance.GetType().GetProperty(propertyNameNext);
                var propertyValueNext = propertyInfoNext?.GetValue(instance) ??
                                        Activator.CreateInstance(propertyInfoNext!.PropertyType);
                propertyInfoNext.SetValue(instance, propertyValueNext);

                SetValueRecursive(propertyValueNext!, parts, index + 1, value, keyPartNext);
            }
            else
            {
                var instanceType = instance.GetType();
                object? nextValue;
                if (typeof(IDictionary).IsAssignableFrom(instanceType))
                {
                    var dictionary = (IDictionary)instance!;
                    var keyType = instanceType.GetGenericArguments()[0];
                    var keyObject = ConvertKeyType(keyPartNext, keyType);
                    
                    nextValue = dictionary.Contains(keyObject) 
                        ? dictionary[keyObject]
                        : Activator.CreateInstance(instanceType.GetGenericArguments()[1])!;
                    dictionary[keyObject] = nextValue;
                    SetValueRecursive(nextValue!, parts, index + 1, value, null);
                }
                else if (typeof(IList).IsAssignableFrom(instanceType))
                {
                    var list = (IList)instance!;
                    var indexValue = (int)ConvertKeyType(keyPartNext, typeof(int));
                    nextValue = Activator.CreateInstance(instanceType.GetGenericArguments()[0])!;
                    while (list.Count <= indexValue)
                    {
                        list.Add(Activator.CreateInstance(instanceType.GetGenericArguments()[0])!);
                    }
                    list[indexValue] = nextValue;
                    SetValueRecursive(nextValue, parts, index + 1, value, null);
                }
            }
        }
        else
        {
            var getter = PropertyGettersCache.GetOrAdd($"{instance.GetType().FullName}.{nextPart}", path => CreatePropertyGetter(instance.GetType(), nextPart));
            
            var propertyInfo = instance.GetType().GetProperty(nextPart)
                               ?? throw new InvalidOperationException($"Property '{nextPart}' not found on type {instance.GetType().FullName}.");
            
            var nextInstance = getter(instance) ?? Activator.CreateInstance(propertyInfo.PropertyType);

            var setter = PropertySettersCache.GetOrAdd($"{instance.GetType().FullName}.{nextPart}", path => CreatePropertySetter(instance.GetType(), nextPart));
            setter(instance, nextInstance);

            SetValueRecursive(nextInstance!, parts, index + 1, value, null);
        }
    }

    private static bool IsIndexedPart(string part, out string propertyName, out string keyPart)
    {
        var openBracket = part.IndexOf('[');
        if (openBracket != -1)
        {
            propertyName = part.Substring(0, openBracket);
            keyPart = part.Substring(openBracket + 1, part.Length - openBracket - 2).Trim('"');
            return true;
        }
        propertyName = part;
        keyPart = string.Empty;
        return false;
    }

    private static object ConvertKeyType(string keyPart, Type targetType)
    {
        if (targetType == typeof(string)) return keyPart;
        if (targetType == typeof(int)) return int.Parse(keyPart);
        if (targetType == typeof(long)) return long.Parse(keyPart);
        if (targetType == typeof(Guid)) return Guid.Parse(keyPart);
        if (targetType.IsEnum) return Enum.Parse(targetType, keyPart);

        throw new InvalidOperationException($"Unsupported key type: {targetType}");
    }

    private static Func<object, object?> CreatePropertyGetter(Type type, string propertyName)
    {
        var param = Expression.Parameter(typeof(object), "instance");
        var typedParam = Expression.Convert(param, type);
        var property = Expression.Property(typedParam, propertyName);
        var castProperty = Expression.Convert(property, typeof(object));
        return Expression.Lambda<Func<object, object?>>(castProperty, param).Compile();
    }

    private static Action<object, object?> CreatePropertySetter(Type type, string propertyName)
    {
        var param = Expression.Parameter(typeof(object), "instance");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var typedParam = Expression.Convert(param, type);
        var property = Expression.Property(typedParam, propertyName);
        var castValue = Expression.Convert(valueParam, property.Type);
        var assign = Expression.Assign(property, castValue);
        return Expression.Lambda<Action<object, object?>>(assign, param, valueParam).Compile();
    }
    
    private sealed class DotNotationVisitor : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression memberExpression)
        {
            // Recurse down to see if we can simplify...
            var expression = Visit(memberExpression.Expression);

            // If we've ended up with a constant, and it's a property or a field,
            // we can simplify ourselves to a constant
            if (expression is not ConstantExpression constantExpression)
                return base.VisitMember(memberExpression);

            var container = constantExpression.Value;
            var member = memberExpression.Member;
            switch (member)
            {
                case FieldInfo fi:
                {
                    var value = fi.GetValue(container);
                    return Expression.Constant(value, fi.FieldType);
                }
                case PropertyInfo propertyInfo:
                {
                    var value = propertyInfo.GetValue(container, null);
                    return Expression.Constant(value);
                }
                default:
                    return base.VisitMember(memberExpression);
            }
        }
    }
}