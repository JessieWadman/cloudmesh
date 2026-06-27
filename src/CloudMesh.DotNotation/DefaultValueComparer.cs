using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace CloudMesh;

public static class DefaultValueComparer
{
    private static readonly ConcurrentDictionary<Type, Func<object, bool>> Checkers = new();

    public static bool IsDefaultValue(object? value, bool emptyStringsAsDefault = false)
    {
        if (value is null)
            return true;

        if (value is string s)
            return emptyStringsAsDefault && s.Length == 0;

        var type = value.GetType();

        // Non-null reference types can never be default(T), because default(T) is null.
        if (!type.IsValueType)
            return false;

        return Checkers.GetOrAdd(type, static t =>
        {
            var method = typeof(DefaultValueComparer)
                .GetMethod(nameof(CreateChecker), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(t);

            return (Func<object, bool>)method.Invoke(null, null)!;
        })(value);
    }

    private static Func<object, bool> CreateChecker<T>() where T : struct
        => static value => EqualityComparer<T>.Default.Equals(Unsafe.Unbox<T>(value), default);
}