using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace CloudMesh;

/// <summary>
/// Determines whether a boxed value equals the default value of its runtime type
/// (<see langword="null"/> for reference types, <c>default(T)</c> for value types) without knowing that
/// type at compile time.
/// </summary>
/// <remarks>
/// For value types the comparison uses <see cref="EqualityComparer{T}.Default"/> against
/// <c>default(T)</c>, resolved once per type and cached in a thread-safe dictionary so subsequent checks
/// avoid reflection.
/// </remarks>
public static class DefaultValueComparer
{
    private static readonly ConcurrentDictionary<Type, Func<object, bool>> Checkers = new();

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is the default value of its type.
    /// </summary>
    /// <param name="value">The (possibly boxed) value to test.</param>
    /// <param name="emptyStringsAsDefault">
    /// When <see langword="true"/>, an empty <see cref="string"/> is also treated as default. A non-empty
    /// string is never default; a <see langword="null"/> string is always default regardless of this flag.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the value is <see langword="null"/>, or equals <c>default(T)</c> for its
    /// value type, or (when <paramref name="emptyStringsAsDefault"/> is set) is an empty string; otherwise
    /// <see langword="false"/>. A non-null reference-type instance is never default.
    /// </returns>
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