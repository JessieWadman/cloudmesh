using System.Runtime.CompilerServices;
using Xunit;

namespace CloudMesh.Variant.Tests;

public readonly struct Small
{
    public Small()
    {
        A = Guid.NewGuid();
    }
    public Guid A { get; }
}

public readonly struct Large
{
    public Large()
    {
        A = Guid.NewGuid();
        B = Guid.NewGuid();
        C = Guid.NewGuid();
        D = Guid.NewGuid();
        E = Guid.NewGuid();
        F = Guid.NewGuid();
    }
    public Guid A { get; }
    public Guid B { get; }
    public Guid C { get; }
    public Guid D { get; }
    public Guid E { get; }
    public Guid F { get; }
}

public class InlineStructVariantTests
{
    [Fact]
    public void TestVariantBoxing()
    {
        // Non-boxing
        var small = new Small();
        
        // Boxing
        var large = new Large();
        
        var smallValue = Value.Create(small);
        var largeValue = Value.Create(large);
        
        var roundtripSmall = smallValue.As<Small>();
        var roundtripLarge = largeValue.As<Large>();
        
        Assert.Equal(roundtripSmall.A, small.A);
        Assert.Equal(roundtripLarge.A, large.A);
        Assert.Equal(roundtripLarge.B, large.B);
        Assert.Equal(roundtripLarge.C, large.C);
        Assert.Equal(roundtripLarge.D, large.D);
        Assert.Equal(roundtripLarge.E, large.E);
        Assert.Equal(roundtripLarge.F, large.F);
        
        // Union should be capable of holding small records. 32 bytes is arbitrary and is likely to change in the future.
        Assert.Equal(16, Unsafe.SizeOf<Value.Union>());
        
        // Small records should be stored inline
        Assert.IsType<Value.StraightCastFlag<Small>>(smallValue._object);
        
        // Large records should be boxed
        Assert.IsType<Large>(largeValue._object);
    }

    [Fact]
    public void NullableInlineStruct_RoundTrips_FromNonNullable()
    {
        var small = new Small();
        var value = Value.Create(small); // stored non-nullable, inline
        Assert.IsType<Value.StraightCastFlag<Small>>(value._object);

        var nullable = value.As<Small?>();
        Assert.True(nullable.HasValue);
        Assert.Equal(small.A, nullable!.Value.A);
    }

    [Fact]
    public void NullableGuid_RoundTrips_FromNonNullable()
    {
        var guid = Guid.NewGuid();
        var value = Value.Create(guid);

        var nullable = value.As<Guid?>();
        Assert.True(nullable.HasValue);
        Assert.Equal(guid, nullable!.Value);
    }

    [Fact]
    public void NullValue_As_NullableInlineStruct_ReturnsNull()
    {
        Value value = default;
        var nullable = value.As<Small?>();
        Assert.False(nullable.HasValue);
    }

    private enum ByteEnum : byte { Red = 1, Green = 2 }
    private enum LongEnum : long { A = 1, B = 9_000_000_000 }

    [Fact]
    public void NullableEnum_RoundTrips_FromNonNullable()
    {
        var small = Value.Create(ByteEnum.Green).As<ByteEnum?>();
        Assert.True(small.HasValue);
        Assert.Equal(ByteEnum.Green, small!.Value);

        var large = Value.Create(LongEnum.B).As<LongEnum?>();
        Assert.True(large.HasValue);
        Assert.Equal(LongEnum.B, large!.Value);
    }

    [Fact]
    public void NullValue_As_NullableEnum_ReturnsNull()
    {
        Value value = default;
        Assert.False(value.As<ByteEnum?>().HasValue);
    }
}