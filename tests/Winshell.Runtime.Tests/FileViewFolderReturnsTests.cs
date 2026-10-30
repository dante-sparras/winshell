using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class FileViewFolderReturnsTests
{
    [Fact]
    public void A_file_in_a_folder_deleted_and_made_again_is_applied()
    {
        const string Qml = """
            import Winshell
            import Winshell.Io
            Scope {
              property alias settings: adapter
              FileView {
                path: Shell.shellDir + "/config/settings.toml"
                blockLoading: true
                watchChanges: true
                onFileChanged: reload()
                TomlAdapter { id: adapter; property string name: "default" }
              }
            }
            """;
        // Declared first, so it is disposed last. A watch can post until the shell is disposed.
        using var woken = new AutoResetEvent(false);
        using var shell = new TestShell(("shell.qml", Qml), ("config/settings.toml", "name = \"first\""));
        var root = shell.Load();
        Assert.Equal("first", shell.Eval(root, "settings.name"));
        var directory = Path.Combine(shell.Directory, "config");
        shell.Loop.Woken += () => woken.Set();

        // The view hears about the deletion when this watch does, so it is handled before the folder returns.
        using var deleted = new ManualResetEventSlim();
        using var watch = DirectoryWatch.Start(directory, DirectoryWatch.FileChanges, _ => { }, deleted.Set);
        Assert.NotNull(watch);
        Directory.Delete(directory, true);
        Assert.True(deleted.Wait(TimeSpan.FromSeconds(30)), "The folder was not reported as deleted.");
        shell.Loop.Process();

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.toml"), "name = \"second\"");

        while (!Equals(shell.Eval(root, "settings.name"), "second"))
        {
            Assert.True(woken.WaitOne(TimeSpan.FromSeconds(30)), "The file in the new folder was not noticed.");
            shell.Loop.Process();
        }
        Assert.Empty(shell.Messages);
    }
}
