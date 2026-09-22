using Xunit;

namespace CloudMesh.Variant.Tests;

public class GuidTests
{
    [Fact]
    public void TestGuids()
    {
        var random = Guid.NewGuid();
        
        var test = Value.Create(random);
        var output = test.As<Guid?>();
        Assert.Equal(random, output);
        Assert.IsType<Value.StraightCastFlag<Guid>>(test._object);
        
        var empty = Guid.Empty;
        var nullGuid = (Guid?)null;
        var nullableGuid = (Guid?)Guid.NewGuid();

        Value randomGuidValue = random;
        Value emptyGuidValue = empty;
        Value nullGuidValue = nullGuid;
        Value nullableGuidValue = nullableGuid;

        Value copiedRandomValue = randomGuidValue;
        Value copiedEmptyValue = emptyGuidValue;
        Value copiedNullValue = nullGuidValue;
        Value copiedNullableValue = nullableGuidValue;

        Guid roundTripRandomValue = (Guid)copiedRandomValue;
        Guid roundTripEmptyValue = (Guid)copiedEmptyValue;
        Guid? roundTripNullValue = (Guid?)copiedNullValue;
        Guid? roundTripNullableValue = (Guid?)copiedNullableValue;

        Assert.Equal(random, roundTripRandomValue);
        Assert.Equal(empty, roundTripEmptyValue);
        Assert.Equal(nullGuid, roundTripNullValue);
        Assert.Equal(nullableGuid, roundTripNullableValue);

        // Cast null to valueType
        Assert.Throws<InvalidCastException>(() => (Guid)copiedNullValue);
    }

    [Fact]
    public void NullableGuid_IsStoredInline_NotBoxed()
    {
        // Unsafe.SizeOf<Guid?>() is 20, over the 16-byte union — but only the 16-byte payload is ever stored,
        // because a Value records "no value" as a null _object.
        var expected = Guid.NewGuid();
        Guid? nullable = expected;

        Value value = nullable;

        Assert.IsType<Value.StraightCastFlag<Guid>>(value._object);
        Assert.Equal(typeof(Guid), value.Type);
        Assert.Equal(expected, value.As<Guid?>());
        Assert.Equal(expected, value.As<Guid>());
    }

    [Fact]
    public void NullableGuid_Null_StoresNull()
    {
        Value value = (Guid?)null;

        Assert.Null(value._object);
        Assert.Null(value.Type);
        Assert.Null(value.As<Guid?>());
    }

    [Fact]
    public void NullableGuid_DoesNotBoxPerElement()
    {
        Guid? g = Guid.NewGuid();
        var values = new Value[100];

        for (var i = 0; i < values.Length; i++) values[i] = g;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < values.Length; i++) values[i] = g;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
