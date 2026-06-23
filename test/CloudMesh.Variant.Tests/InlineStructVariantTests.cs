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
        Assert.Equal(32, Unsafe.SizeOf<Value.Union>());
        
        // Small records should be stored inline
        Assert.IsType<Value.InlineStructFlag<Small>>(smallValue._object);
        
        // Large records should be boxed
        Assert.IsType<Large>(largeValue._object);
    }
}