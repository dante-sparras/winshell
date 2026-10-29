using Winshell.Runtime;
using Xunit;

namespace Winshell.Runtime.Tests;

public class NumericConversionTests
{
    [Fact]
    public void Every_csharp_number_converts_to_a_double()
    {
        Assert.Equal(7.0, ValueConverter.ToDouble(7u, "real"));
        Assert.Equal(-7.0, ValueConverter.ToDouble((short)-7, "real"));
        Assert.Equal(7.0, ValueConverter.ToDouble((ushort)7, "real"));
        Assert.Equal(7.0, ValueConverter.ToDouble((byte)7, "real"));
        Assert.Equal(-7.0, ValueConverter.ToDouble((sbyte)-7, "real"));
        Assert.Equal(7.0, ValueConverter.ToDouble(7ul, "real"));
        Assert.Equal(7.5, ValueConverter.ToDouble(7.5m, "real"));
    }

    [Fact]
    public void A_zero_of_any_number_type_is_false()
    {
        Assert.False(ValueConverter.ToBool(0L));
        Assert.False(ValueConverter.ToBool(0f));
        Assert.False(ValueConverter.ToBool(float.NaN));
        Assert.False(ValueConverter.ToBool(0m));
        Assert.False(ValueConverter.ToBool((byte)0));
        Assert.True(ValueConverter.ToBool(2L));
        Assert.True(ValueConverter.ToBool(0.5f));
    }

    [Fact]
    public void An_int_converts_to_a_color_like_a_double_does()
    {
        Assert.Equal(ValueConverter.ToColor((double)0xff00ff00u), ValueConverter.ToColor(unchecked((int)0xff00ff00)));
    }

    [Fact]
    public void A_long_from_nan_or_infinity_is_zero_like_an_int()
    {
        Assert.Equal(0L, ValueConverter.Convert(double.NaN, typeof(long), null));
        Assert.Equal(0L, ValueConverter.Convert(double.PositiveInfinity, typeof(long), null));
        Assert.Equal(-3L, ValueConverter.Convert(-3.7, typeof(long), null));
    }

    [Fact]
    public void A_number_formats_as_javascript_does()
    {
        Assert.Equal("1e-7", ValueConverter.FormatNumber(1e-7));
        Assert.Equal("1.5e+300", ValueConverter.FormatNumber(1.5e300));
        Assert.Equal("1e+21", ValueConverter.FormatNumber(1e21));
        Assert.Equal("100000000000000000000", ValueConverter.FormatNumber(1e20));
        Assert.Equal("0.1", ValueConverter.FormatNumber(0.1));
        Assert.Equal("0", ValueConverter.FormatNumber(-0.0));
        Assert.Equal("-Infinity", ValueConverter.FormatNumber(double.NegativeInfinity));
        Assert.Equal("NaN", ValueConverter.FormatNumber(double.NaN));
    }

    [Fact]
    public void A_small_number_assigned_to_a_string_property_reads_as_javascript_prints_it()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property string text: 1e-7
            }
            """));
        var root = shell.Load();
        Assert.Equal("1e-7", root.ReadProperty("text"));
        Assert.Equal("1e-7", shell.Eval(root, "String(1e-7)"));
    }
}
