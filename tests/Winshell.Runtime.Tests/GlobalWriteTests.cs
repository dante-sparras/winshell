namespace Winshell.Runtime.Tests;

/// <summary>A typo assignment does not become a global, and Qt cannot be replaced.</summary>
public class GlobalWriteTests
{
    [Fact]
    public void A_typo_assignment_does_not_create_a_global()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property bool created: true
              property string json: JSON.stringify(1)
              property int n: 1
              Component.onCompleted: {
                typo = 7;
                created = (typeof typo !== "undefined");
                n = 4;
              }
            }
            """));
        var root = shell.Load();

        Assert.Equal(false, root.ReadProperty("created"));
        Assert.Equal("1", root.ReadProperty("json"));
        Assert.Equal(4, root.ReadProperty("n"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Replacing_Qt_or_console_leaves_them_in_place()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property int x: 0
              Component.onCompleted: {
                Qt = 1;
                console = 1;
                x = Qt.point(2, 3).x;
                console.log("still here");
              }
            }
            """));
        var root = shell.Load();

        Assert.Equal(2, root.ReadProperty("x"));
        Assert.Contains(shell.Messages, message => message.Contains("still here", StringComparison.Ordinal));
    }
}
