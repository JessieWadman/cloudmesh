using Xunit;

namespace CloudMesh.Variant.Tests;

// decimal is a 16-byte unmanaged value type and fits the 16-byte inline union (Guid, also 16 bytes, is inlined).
// It must be stored inline via StraightCastFlag<decimal>, NOT boxed as an object reference.
public class DecimalInlineTests
{
    [Fact]
    public void Decimal_IsStoredInline_NotBoxed()
    {
        const decimal d = 12345.6789m;
        Value value = d; // implicit operator

        Assert.IsType<Value.StraightCastFlag<decimal>>(value._object); // inline flag, not a boxed decimal
        Assert.Equal(typeof(decimal), value.Type);
    }

    [Fact]
    public void Decimal_RoundTrips()
    {
        const decimal d = -0.0000000001m;
        Value value = d;

        Assert.Equal(d, value.As<decimal>());
        Assert.Equal(d, (decimal)value);
    }

    [Fact]
    public void NullableDecimal_RoundTrips_FromValue()
    {
        // A *nullable* 16-byte value type (decimal?, like Guid?) exceeds the 16-byte union once the has-value
        // flag is included, so it boxes — consistent with Guid?. Only the non-nullable form stores inline.
        decimal? d = 42.5m;
        Value value = d;

        var back = value.As<decimal?>();
        Assert.True(back.HasValue);
        Assert.Equal(42.5m, back!.Value);
    }

    [Fact]
    public void NullableDecimal_Null_StoresNull()
    {
        decimal? d = null;
        Value value = d;

        Assert.Null(value.As<decimal?>());
    }

    [Fact]
    public void Decimal_StoredIntoArray_DoesNotBoxPerElement()
    {
        var decimals = new decimal[100];
        for (var i = 0; i < decimals.Length; i++) decimals[i] = i * 1.5m;
        var values = new Value[100];

        // Warm up the JIT and any first-use caches.
        for (var i = 0; i < decimals.Length; i++) values[i] = decimals[i];

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < decimals.Length; i++) values[i] = decimals[i];
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated); // would be ~100 boxes before the inline fix
    }

    [Fact]
    public void Value_And_Union_Sizes_AreAsDocumented()
    {
        Assert.Equal(16, System.Runtime.CompilerServices.Unsafe.SizeOf<Value.Union>());
        Assert.Equal(24, System.Runtime.CompilerServices.Unsafe.SizeOf<Value>()); // 8-byte object ref + 16-byte union
    }
}
