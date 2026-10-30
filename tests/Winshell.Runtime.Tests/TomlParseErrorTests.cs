namespace Winshell.Runtime.Tests;

/// <summary>A TOML file that does not parse keeps its bytes, names the line, and tells the view the write was dropped.</summary>
public class TomlParseErrorTests
{
    private const string Qml = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias settings: adapter
          property int failed: 0
          FileView {
            id: view
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            blockWrites: true
            printErrors: false
            onSaveFailed: function(error) { failed = failed + 1 }
            onAdapterUpdated: writeAdapter()
            TomlAdapter {
              id: adapter
              property string name: "default"
              property bool on: true
            }
          }
        }
        """;

    [Fact]
    public void A_file_that_does_not_parse_names_the_line_and_blocks_the_write()
    {
        using var shell = new TestShell(("shell.qml", Qml), ("settings.toml", "name = \"broken"));
        var root = shell.Load();
        var error = Assert.IsType<string>(shell.Eval(root, "settings.error"));
        Assert.Contains("settings.toml[1:", error, StringComparison.Ordinal);
        Assert.Contains("]:", error, StringComparison.Ordinal);
        Assert.Contains(shell.Messages, m => m.Contains("settings.toml[1:", StringComparison.Ordinal));

        var path = Path.Combine(shell.Directory, "settings.toml");
        var before = File.ReadAllBytes(path);
        shell.Eval(root, "settings.on = false");
        shell.Loop.Process();

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(1, Convert.ToInt32(shell.Eval(root, "failed"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(shell.Messages, m => m.Contains("left unchanged", StringComparison.Ordinal));

        File.WriteAllText(path, "name = \"ok\"\n");
        shell.Eval(root, "view.reload()");
        shell.Loop.Process();
        Assert.Equal("", shell.Eval(root, "settings.error"));
        Assert.Equal("ok", shell.Eval(root, "settings.name"));
    }
}
