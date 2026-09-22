#if NET11_0_OR_GREATER
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace CloudMesh.Variant.Tests;

/// <summary>
/// Covers the .NET 11 union contract implemented by <see cref="Value"/>: the case types, the implicit union
/// conversions, the non-boxing access pattern, and the boxing <c>Value</c> accessor.
/// </summary>
public class UnionTests
{
    [Fact]
    public void ValueIsAUnionType()
    {
        Assert.NotNull(typeof(Value).GetCustomAttribute<UnionAttribute>());
        Assert.True(typeof(IUnion).IsAssignableFrom(typeof(Value)));
        Assert.True(typeof(Value.IUnionMembers).IsAssignableFrom(typeof(Value)));
    }

    [Fact]
    public void TypePatternMatchesTheStoredValue()
    {
        Value value = 42;

        Assert.True(value is int);
        Assert.False(value is long);
        Assert.False(value is string);

        Assert.True(value is 42);
        Assert.True(value is int and > 40);
    }

    [Fact]
    public void SwitchExpressionDispatchesOnCaseType()
    {
        Assert.Equal("int: 42", Describe(42));
        Assert.Equal("double: 3.5", Describe(3.5));   // formatted invariantly below
        Assert.Equal("string: hello", Describe("hello"));
        Assert.Equal("null", Describe(default));

        static string Describe(Value value) => value switch
        {
            int i => $"int: {i}",
            double d => $"double: {d.ToString(CultureInfo.InvariantCulture)}",
            string s => $"string: {s}",
            null => "null",
            object o => $"other: {o}",
        };
    }

    [Fact]
    public void NullPatternMatchesAStoredNull()
    {
        Value none = default;
        Value some = 1;

        Assert.True(none is null);
        Assert.False(some is null);
        Assert.False(none.HasValue);
        Assert.True(some.HasValue);
    }

    [Fact]
    public void ValuePropertyBoxesTheStoredValue()
    {
        Assert.Equal(42, ((IUnion)(Value)42).Value);
        Assert.Equal(Guid.Empty, ((IUnion)Value.Create(Guid.Empty)).Value);
        Assert.Equal("hello", ((IUnion)new Value("hello")).Value);
        Assert.Null(((IUnion)default(Value)).Value);
    }

    [Theory]
    [MemberData(nameof(CaseTypeValues))]
    public void EveryCaseTypeRoundTripsThroughTheValueProperty(object expected)
    {
        var value = Value.Create(expected);

        Assert.Equal(expected, ((IUnion)value).Value);
        Assert.True(value.HasValue);
    }

    public static TheoryData<object> CaseTypeValues() =>
    [
        true,
        (byte)1,
        (sbyte)-1,
        'c',
        (short)-2,
        (ushort)2,
        3,
        (uint)3,
        4L,
        (ulong)4,
        5.5f,
        6.5d,
        7.5m,
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        new DateTime(2026, 9, 22, 1, 2, 3, DateTimeKind.Utc),
        new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromHours(2)),
        "text",
    ];

    [Fact]
    public void NullableCaseTypeWithAValueMatchesTheUnderlyingType()
    {
        int? maybe = 42;
        Value value = maybe;

        Assert.True(value.HasValue);
        Assert.True(value is int);
        Assert.False(value is null);

        Assert.Equal("int: 42", value switch
        {
            int i => $"int: {i}",
            null => "null",
            object o => $"other: {o}",
        });
    }

    [Fact]
    public void NullableCaseTypeWithoutAValueMatchesNull()
    {
        int? maybe = null;
        Value value = maybe;

        Assert.False(value.HasValue);
        Assert.True(value is null);
        Assert.False(value is int);

        Assert.Equal("null", value switch
        {
            int i => $"int: {i}",
            null => "null",
            object o => $"other: {o}",
        });
    }

    [Theory]
    [MemberData(nameof(NullableCaseTypeValues))]
    public void EveryNullableCaseTypeRoundTripsThroughTheValueProperty(object underlying, Value withValue, Value withoutValue)
    {
        Assert.Equal(underlying, ((IUnion)withValue).Value);
        Assert.True(withValue.HasValue);

        Assert.Null(((IUnion)withoutValue).Value);
        Assert.False(withoutValue.HasValue);
        Assert.True(withoutValue is null);
    }

    public static TheoryData<object, Value, Value> NullableCaseTypeValues() => new()
    {
        { true, (bool?)true, (bool?)null },
        { (byte)1, (byte?)1, (byte?)null },
        { (sbyte)-1, (sbyte?)-1, (sbyte?)null },
        { 'c', (char?)'c', (char?)null },
        { (short)-2, (short?)-2, (short?)null },
        { (ushort)2, (ushort?)2, (ushort?)null },
        { 3, (int?)3, (int?)null },
        { (uint)3, (uint?)3, (uint?)null },
        { 4L, (long?)4L, (long?)null },
        { (ulong)4, (ulong?)4, (ulong?)null },
        { 5.5f, (float?)5.5f, (float?)null },
        { 6.5d, (double?)6.5d, (double?)null },
        { 7.5m, (decimal?)7.5m, (decimal?)null },
        {
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            (Guid?)Guid.Parse("11111111-2222-3333-4444-555555555555"),
            (Guid?)null
        },
        {
            new DateTime(2026, 9, 22, 1, 2, 3, DateTimeKind.Utc),
            (DateTime?)new DateTime(2026, 9, 22, 1, 2, 3, DateTimeKind.Utc),
            (DateTime?)null
        },
        {
            new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromHours(2)),
            (DateTimeOffset?)new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromHours(2)),
            (DateTimeOffset?)null
        },
    };

    [Fact]
    public void NullableSourceStillStoresInline()
    {
        int? maybe = 42;
        Value value = maybe;

        Assert.Same(Value.TypeFlags.Int32, value._object);
        Assert.Equal(typeof(int), value.Type);
    }

    [Fact]
    public void MatchingAValueFromANullableSourceDoesNotAllocate()
    {
        int? maybe = 42;
        Value value = maybe;

        _ = Match(value, iterations: 1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var matched = Match(value, iterations: 10_000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(10_000, matched);
        Assert.Equal(0, allocated);

        static int Match(Value value, int iterations)
        {
            var matched = 0;

            for (var i = 0; i < iterations; i++)
            {
                if (value is int) matched++;
            }

            return matched;
        }
    }

    [Fact]
    public void TryGetValueOverloadsAgreeWithTheGenericForm()
    {
        Value value = 42;

        Assert.True(value.TryGetValue(out int i));
        Assert.Equal(42, i);

        Assert.False(value.TryGetValue(out long _));
        Assert.False(value.TryGetValue(out string? _));

        Assert.True(value.TryGetValue(out object? boxed));
        Assert.Equal(42, boxed);

        Assert.False(default(Value).TryGetValue(out object? _));
    }

    [Fact]
    public void ImplicitUnionConversionStoresInlineNotBoxed()
    {
        // The user-defined conversion operator wins over the union conversion, so an int still lands in the
        // inline union rather than being boxed into _object.
        Value value = 42;

        Assert.Same(Value.TypeFlags.Int32, value._object);
        Assert.Equal(typeof(int), value.Type);
    }

    [Fact]
    public void PatternMatchingAnInlineValueDoesNotAllocate()
    {
        Value value = 42;

        // Warm up so JIT compilation is not counted.
        _ = Sum(value, iterations: 1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var total = Sum(value, iterations: 10_000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(420_000, total);
        Assert.Equal(0, allocated);

        static long Sum(Value value, int iterations)
        {
            long total = 0;

            for (var i = 0; i < iterations; i++)
            {
                total += value switch
                {
                    int n => n,
                    long n => n,
                    null => 0,
                    object => -1,
                };
            }

            return total;
        }
    }
}
#endif
