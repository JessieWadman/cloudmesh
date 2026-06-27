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
}