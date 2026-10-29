using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class ExpoEasingEndsTests
{
    [Theory]
    [InlineData("InExpo")]
    [InlineData("OutExpo")]
    [InlineData("InOutExpo")]
    [InlineData("OutInExpo")]
    public void An_expo_curve_starts_at_exactly_0_and_ends_at_exactly_1(string name)
    {
        var type = Array.IndexOf(Easing.Names, name);

        Assert.Equal(0.0, Easing.Value(type, 0));
        Assert.Equal(1.0, Easing.Value(type, 1));
    }
}
