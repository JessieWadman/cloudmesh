using Xunit;

namespace CloudMesh.Variant.Tests;

// Regression coverage for Wadman-IT/Primodel#732: a non-whole-hour offset (e.g. +02:30) round-tripped as
// +02:02, because PackedDateTimeOffset.Extract used the raw 0-3 quarter-hour index as a minute count instead
// of scaling it by 15, and TryCreate separately corrupted the packed bits for any negative offset with a
// nonzero minute remainder by left-shifting a negative int cast to ulong.
public class DateTimeOffsetTests
{
    public static TheoryData<DateTimeOffset> QuarterHourOffsets() =>
    [
        new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromMinutes(330)),   // +05:30
        new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromMinutes(345)),   // +05:45
        new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromMinutes(-210)),  // -03:30
        new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromMinutes(765)),   // +12:45
        new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromMinutes(840)),   // +14:00
        new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromMinutes(-720)),  // -12:00
        new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromMinutes(-840)),  // -14:00
    ];

    [Theory]
    [MemberData(nameof(QuarterHourOffsets))]
    public void RoundTrips_ThroughCreateAndAs(DateTimeOffset expected)
    {
        var value = Value.Create(expected);

        Assert.Equal(expected, value.As<DateTimeOffset>());
        Assert.Equal(expected.Offset, value.As<DateTimeOffset>().Offset);
    }

    [Theory]
    [MemberData(nameof(QuarterHourOffsets))]
    public void RoundTrips_ThroughImplicitAndExplicitOperators(DateTimeOffset expected)
    {
        Value value = expected;
        var actual = (DateTimeOffset)value;

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Offset, actual.Offset);
    }

    [Theory]
    [MemberData(nameof(QuarterHourOffsets))]
    public void RoundTrips_AsNullable(DateTimeOffset expected)
    {
        DateTimeOffset? nullable = expected;
        Value value = nullable;

        Assert.Equal(expected, value.As<DateTimeOffset?>());
        Assert.Equal(expected, (DateTimeOffset?)value);
        Assert.Equal(expected.Offset, value.As<DateTimeOffset?>()!.Value.Offset);
    }

    [Theory]
    [MemberData(nameof(QuarterHourOffsets))]
    public void RoundTrips_ThroughTryGetValue(DateTimeOffset expected)
    {
        Value value = expected;

        Assert.True(value.TryGetValue(out DateTimeOffset actual));
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Offset, actual.Offset);
    }

    [Fact]
    public void NonQuarterHourOffset_FallsBackToBoxing_AndStillRoundTrips()
    {
        // 13 minutes is not a multiple of 15, so it can't use the packed fast path; it must still round-trip
        // exactly via the boxed fallback (Value.cs: `else { _object = value; }`).
        var expected = new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromMinutes(13));

        var value = Value.Create(expected);

        Assert.Equal(expected, value.As<DateTimeOffset>());
        Assert.IsType<DateTimeOffset>(value._object);
    }

    [Fact]
    public void UtcOffset_IsStoredViaTheUnpackedFlag_NotThePackedOne()
    {
        var expected = new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.Zero);

        var value = Value.Create(expected);

        Assert.Same(Value.TypeFlags.DateTimeOffset, value._object);
        Assert.Equal(expected, value.As<DateTimeOffset>());
    }

    [Theory]
    [InlineData(330)]   // +05:30
    [InlineData(-210)]  // -03:30
    [InlineData(765)]   // +12:45
    public void NonZeroQuarterHourOffset_IsStoredViaThePackedFlag(int offsetMinutes)
    {
        var value = Value.Create(new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.FromMinutes(offsetMinutes)));

        Assert.Same(Value.TypeFlags.PackedDateTimeOffset, value._object);
    }

    [Fact]
    public void LeapDay_RoundTrips()
    {
        var expected = new DateTimeOffset(2024, 2, 29, 12, 0, 0, TimeSpan.FromMinutes(-210)); // -03:30

        var value = Value.Create(expected);

        Assert.Equal(expected, value.As<DateTimeOffset>());
    }

    [Fact]
    public void DateTimeOffsetMinValue_RoundTrips()
    {
        var value = Value.Create(DateTimeOffset.MinValue);

        Assert.Equal(DateTimeOffset.MinValue, value.As<DateTimeOffset>());
    }

    [Fact]
    public void DateTimeOffsetMaxValue_RoundTrips()
    {
        var value = Value.Create(DateTimeOffset.MaxValue);

        Assert.Equal(DateTimeOffset.MaxValue, value.As<DateTimeOffset>());
    }

    [Fact]
    public void NullDateTimeOffset_RoundTrips()
    {
        DateTimeOffset? nullable = null;
        Value value = nullable;

        Assert.Null(value.As<DateTimeOffset?>());
    }
}
