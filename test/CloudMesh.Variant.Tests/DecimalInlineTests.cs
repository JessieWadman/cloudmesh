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
    public void NullableDecimal_IsStoredInline_NotBoxed()
    {
        // Unsafe.SizeOf<decimal?>() is 24 — the has-value flag plus padding pushes it past the 16-byte union.
        // But the flag never needs storing: a Value records "no value" as a null _object, so only the 16-byte
        // payload has to fit. A nullable decimal must therefore store exactly like a non-nullable one.
        decimal? d = 42.5m;
        Value value = d;

        Assert.IsType<Value.StraightCastFlag<decimal>>(value._object);
        Assert.Equal(typeof(decimal), value.Type);
    }

    [Fact]
    public void NullableDecimal_RoundTrips_FromValue()
    {
        decimal? d = 42.5m;
        Value value = d;

        var back = value.As<decimal?>();
        Assert.True(back.HasValue);
        Assert.Equal(42.5m, back!.Value);

        // Stored as the underlying type, so it also reads back non-nullable.
        Assert.Equal(42.5m, value.As<decimal>());
    }

    [Fact]
    public void NullableDecimal_StoredIntoArray_DoesNotBoxPerElement()
    {
        decimal? d = 42.5m;
        var values = new Value[100];

        // Warm up the JIT and any first-use caches.
        for (var i = 0; i < values.Length; i++) values[i] = d;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < values.Length; i++) values[i] = d;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NullableDecimal_ViaRuntimeGenericCreate_IsStoredInline()
    {
        // Create<T> with T known only at runtime cannot pick the Nullable<T> overload, so it relies on the
        // typeof(T) == typeof(decimal?) fast path instead of the size check, which would box.
        var value = Value.Create<decimal?>(42.5m);

        Assert.IsType<Value.StraightCastFlag<decimal>>(value._object);
        Assert.Equal(42.5m, value.As<decimal>());
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
