namespace Winshell.Runtime.Tests;

/// <summary>Qt arrow values and Qt.ColorScheme match Qt's numbers.</summary>
public class QtEnumTests
{
    [Fact]
    public void Arrow_and_color_scheme_values_match_Qt()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property int no: Qt.NoArrow
              property int up: Qt.UpArrow
              property int down: Qt.DownArrow
              property int left: Qt.LeftArrow
              property int right: Qt.RightArrow
              property int unknown: Qt.ColorScheme.Unknown
              property int light: Qt.ColorScheme.Light
              property int dark: Qt.ColorScheme.Dark
            }
            """));
        var root = shell.Load();

        Assert.Equal(0, root.ReadProperty("no"));
        Assert.Equal(1, root.ReadProperty("up"));
        Assert.Equal(2, root.ReadProperty("down"));
        Assert.Equal(3, root.ReadProperty("left"));
        Assert.Equal(4, root.ReadProperty("right"));
        Assert.Equal(0, root.ReadProperty("unknown"));
        Assert.Equal(1, root.ReadProperty("light"));
        Assert.Equal(2, root.ReadProperty("dark"));
        Assert.Empty(shell.Messages);
    }
}
