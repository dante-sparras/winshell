using Jint.Native;
using Winshell.Runtime;
using Xunit;

namespace Winshell.Runtime.Tests;

public class KeepJsTests
{
    [Fact]
    public void A_var_keeps_a_plain_object_and_a_point_stays_a_point()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property var bag: ({ name: "a", nums: [1, 2] })
              property var spot: Qt.point(2, 3)
              property var tint: Qt.rgba(1, 0, 0, 1)
            }
            """));
        var root = shell.Load();
        var bag = root.ReadProperty("bag");
        Assert.IsAssignableFrom<JsValue>(bag);
        Assert.IsNotType<QPoint>(bag);

        Assert.Equal("b", shell.Eval(root, "bag.name = \"b\""));
        Assert.Equal(3.0, shell.Eval(root, "bag.nums.push(3)"));
        Assert.Equal("b", shell.Eval(root, "bag.name"));
        Assert.Equal(3.0, shell.Eval(root, "bag.nums.length"));

        var spot = Assert.IsType<QPoint>(root.ReadProperty("spot"));
        Assert.Equal(2.0, spot.X);
        Assert.Equal(3.0, spot.Y);
        var tint = Assert.IsType<QColor>(root.ReadProperty("tint"));
        Assert.Equal(1.0, tint.R);
        Assert.Empty(shell.Messages);
    }
}
