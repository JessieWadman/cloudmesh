using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace CloudMesh;

public static class DefaultValueComparer
{
    private static readonly ConcurrentDictionary<Type, Func<object?, bool>> defaultValueCheckers = new();

    public static bool IsDefaultValue(object? value, bool emptyStringsAsDefault = false)
    {
        if (value is string str && emptyStringsAsDefault)
            return string.IsNullOrEmpty(str);
        return value == null || defaultValueCheckers.GetOrAdd(value.GetType(), CreateDefaultValueChecker)(value);
    }

    private static Func<object?, bool> CreateDefaultValueChecker(Type type)
    {
        // Create a strongly-typed EqualityComparer<T> dynamically
        var equalityComparerType = typeof(EqualityComparer<>).MakeGenericType(type);
        var defaultComparerProperty = equalityComparerType.GetProperty("Default")!;
        var equalsMethod = equalityComparerType.GetMethod("Equals", [type, type])!;

        // Get the default value for the given type
        var defaultValue = Expression.Default(type);

        // Create the parameter expression for the input value
        var param = Expression.Parameter(typeof(object), "value");

        // Convert the input parameter from object to the target type
        var castParam = Expression.Convert(param, type);

        // Call the Equals method on the EqualityComparer<T>.Default
        var equalsCall = Expression.Call(
            Expression.Property(null, defaultComparerProperty),
            equalsMethod,
            castParam,
            defaultValue
        );

        // Compile the expression into a Func<object?, bool>
        return Expression.Lambda<Func<object?, bool>>(equalsCall, param).Compile();
    }
}