using Xunit;

namespace CloudMesh.Variant.Tests;

/// <summary>
/// <see cref="Value.ToString"/> must render the carried value, never the wrapper's own type name. Without an
/// override, a struct's default <c>ToString()</c> returns its full type name — a caller who writes
/// <c>value.ToString()</c> instead of unwrapping first (e.g. <c>value.As&lt;T&gt;()</c>) would otherwise see the
/// literal string "CloudMesh.Variant.Value" rendered as if it were data.
/// </summary>
public class ValueToStringTests
{
    [Fact]
    public void Null_renders_as_empty_string()
        => Assert.Equal(string.Empty, new Value((object?)null).ToString());

    [Fact]
    public void Inline_primitive_renders_its_own_value()
        => Assert.Equal("42", Value.Create(42).ToString());

    [Fact]
    public void Inline_unmanaged_struct_renders_its_own_value_not_the_wrapper_type_name()
    {
        var date = new DateOnly(2016, 6, 16);
        var rendered = Value.Create(date).ToString();

        Assert.Equal(date.ToString(), rendered);
        Assert.DoesNotContain("CloudMesh.Variant.Value", rendered);
    }

    [Fact]
    public void DateTime_renders_its_own_value()
    {
        var dt = new DateTime(2016, 6, 16, 12, 30, 0, DateTimeKind.Utc);
        Assert.Equal(dt.ToString(), Value.Create(dt).ToString());
    }

    [Fact]
    public void Reference_value_renders_its_own_value()
        => Assert.Equal("hello", new Value("hello").ToString());

    [Theory]
    [InlineData(42)]
    [InlineData(true)]
    [InlineData(1.5)]
    public void No_stored_value_ever_renders_the_carrier_type_name(object boxed)
    {
        var value = Value.Create(boxed);
        Assert.DoesNotContain("CloudMesh.Variant.Value", value.ToString());
    }
}
