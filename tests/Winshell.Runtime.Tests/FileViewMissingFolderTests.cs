using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class FileViewMissingFolderTests
{
    [Theory]
    [InlineData("later")]
    [InlineData("later/nested/config")]
    public void A_hand_created_file_in_a_new_folder_is_applied(string relativeFolder)
    {
        var qml = $$"""
            import Winshell
            import Winshell.Io
            Scope {
              property alias settings: adapter
              FileView {
                path: Shell.shellDir + "/{{relativeFolder}}/settings.toml"
                blockLoading: true
                watchChanges: true
                onFileChanged: reload()
                TomlAdapter { id: adapter; property string name: "default" }
              }
            }
            """;
        using var shell = new TestShell(("shell.qml", qml));
        var root = shell.Load();
        var directory = Path.Combine(shell.Directory, relativeFolder);
        Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.toml"), "name = \"by hand\"");

        Wait.Until(() => Equals(shell.Eval(root, "settings.name"), "by hand"), "the file in the new folder", shell.Loop.Process);

        Assert.Equal("by hand", shell.Eval(root, "settings.name"));
        Assert.Empty(shell.Messages);
    }
}
