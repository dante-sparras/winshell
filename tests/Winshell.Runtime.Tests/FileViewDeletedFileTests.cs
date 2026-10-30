using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class FileViewDeletedFileTests
{
    private const string Qml = """
        import Winshell
        import Winshell.Io
        Scope {
          property alias settings: adapter
          property int changes: 0
          property int failures: 0
          FileView {
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            watchChanges: true
            onFileChanged: { changes++; reload() }
            onLoadFailed: failures++
            TomlAdapter { id: adapter; property string name: "default" }
          }
        }
        """;

    [Fact]
    public void A_deleted_file_is_reported_and_the_adapter_keeps_its_values()
    {
        // Declared first, so it is disposed last. A watch can post until the shell is disposed.
        using var woken = new AutoResetEvent(false);
        using var shell = new TestShell(("shell.qml", Qml), ("settings.toml", "name = \"first\""));
        var root = shell.Load();
        shell.Loop.Woken += () => woken.Set();

        File.Delete(Path.Combine(shell.Directory, "settings.toml"));

        while (!Equals(shell.Eval(root, "failures"), 1.0))
        {
            Assert.True(woken.WaitOne(TimeSpan.FromSeconds(30)), "The deleted file was not noticed.");
            shell.Loop.Process();
        }
        Assert.Equal("first", shell.Eval(root, "settings.name"));
        Assert.Equal(1.0, shell.Eval(root, "changes"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_not_a_deleted_file()
    {
        using var woken = new AutoResetEvent(false);
        using var shell = new TestShell(("shell.qml", Qml), ("settings.toml", "name = \"first\""));
        var root = shell.Load();
        var path = Path.Combine(shell.Directory, "settings.toml");
        shell.Loop.Woken += () => woken.Set();

        File.WriteAllText(path, "name = \"second\"");
        Assert.True(woken.WaitOne(TimeSpan.FromSeconds(30)), "The write was not noticed.");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            shell.Loop.Process();

        Assert.Equal(0.0, shell.Eval(root, "changes"));
        Assert.Equal(0.0, shell.Eval(root, "failures"));
        Assert.Equal("first", shell.Eval(root, "settings.name"));
    }
}
