using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>An object created with no parent belongs to the engine and dies with that generation.</summary>
public class UnownedObjectTests
{
    [Fact]
    public void An_object_created_with_no_parent_is_destroyed_on_reload()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property var extra
              Component.onCompleted: extra = Qt.createQmlObject('import QtQml; QtObject { objectName: "extra" }', null)
            }
            """));
        using var host = new ShellHost(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, null, shell.Log) { WatchFiles = false };
        Assert.True(host.Start());
        shell.Loop.Process();
        var extra = Assert.IsAssignableFrom<QObject>(host.Root!.ReadProperty("extra"));
        Assert.Equal("extra", extra.ObjectName);
        Assert.False(extra.IsDestroyed);

        host.Reload(hard: false);
        shell.Loop.Process();

        Assert.True(extra.IsDestroyed);
        Assert.False(host.Root!.IsDestroyed);
    }

    [Fact]
    public void A_file_singleton_still_loads_with_the_shell()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                QtObject { objectName: Brand.name }
                """),
            ("Brand.qml", """
                pragma Singleton
                import QtQml
                QtObject { property string name: "brand" }
                """));
        using var host = new ShellHost(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, null, shell.Log) { WatchFiles = false };
        Assert.True(host.Start());
        Assert.Equal("brand", host.Root!.ObjectName);
        Assert.False(host.Root.IsDestroyed);
    }
}
