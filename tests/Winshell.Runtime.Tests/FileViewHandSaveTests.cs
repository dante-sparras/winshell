using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A hand save that lands while <see cref="FileView"/> is writing must win, without waiting on the watcher.</summary>
public class FileViewHandSaveTests
{
    private const string Watching = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias settings: adapter
          FileView {
            path: Shell.shellDir + "/later/settings.toml"
            blockLoading: true
            blockWrites: true
            watchChanges: true
            printErrors: false
            onAdapterUpdated: writeAdapter()
            onFileChanged: reload()
            TomlAdapter {
              id: adapter
              property string name: "default"
              property bool on: true
            }
          }
        }
        """;

    [Fact]
    public void A_hand_save_wins_only_when_the_disk_matches_neither_side_of_the_write()
    {
        Assert.False(FileView.HandSaveWins(null, "a", "b"));
        Assert.False(FileView.HandSaveWins("a", "a", "b"));
        Assert.False(FileView.HandSaveWins("b", "a", "b"));
        Assert.True(FileView.HandSaveWins("c", "a", "b"));
        Assert.True(FileView.HandSaveWins("c", null, "b"));
    }

    [Fact]
    public void A_hand_save_before_the_watcher_is_armed_becomes_the_properties()
    {
        using var shell = new TestShell(("shell.qml", Watching));
        var root = shell.Load();
        var path = Path.Combine(shell.Directory, "later", "settings.toml");
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));

        shell.Eval(root, "settings.name = 'chosen'");
        // The folder and the file exist. The watcher is armed on the next turn, so this save is already too late for it.
        File.WriteAllText(path, "name = \"by hand\"\n");
        shell.Loop.Process();

        Assert.Equal("by hand", shell.Eval(root, "settings.name"));
        Assert.Contains("name = \"by hand\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_save_that_lands_before_the_replace_is_not_overwritten()
    {
        const string Qml = """
            import Winshell
            import Winshell.Io
            import QtQuick
            Scope {
              property alias settings: adapter
              FileView {
                path: Shell.shellDir + "/settings.toml"
                blockLoading: true
                blockWrites: true
                watchChanges: true
                printErrors: false
                onAdapterUpdated: writeAdapter()
                onFileChanged: reload()
                TomlAdapter {
                  id: adapter
                  property string name: "default"
                  property bool on: true
                }
              }
            }
            """;
        using var shell = new TestShell(("shell.qml", Qml), ("settings.toml", "name = \"file\"\n"));
        var root = shell.Load();
        var path = Path.Combine(shell.Directory, "settings.toml");
        FileView.SetBeforeReplaceForTests(written =>
        {
            if (string.Equals(Path.GetFullPath(written), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(path, "name = \"by hand\"\n");
        });
        try
        {
            shell.Eval(root, "settings.on = false");
            shell.Loop.Process();
            Assert.Equal("by hand", shell.Eval(root, "settings.name"));
            var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Equal("name = \"by hand\"\n", text);
            Assert.False(File.Exists(path + ".tmp-" + Environment.ProcessId));
        }
        finally
        {
            FileView.SetBeforeReplaceForTests(null);
        }
    }
}
