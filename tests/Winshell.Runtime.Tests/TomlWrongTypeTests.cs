namespace Winshell.Runtime.Tests;

/// <summary>A string property given another TOML type is left alone, not converted and written back.</summary>
public class TomlWrongTypeTests
{
    private const string Settings = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias settings: adapter
          FileView {
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            blockWrites: true
            onAdapterUpdated: writeAdapter()
            TomlAdapter {
              id: adapter
              property string accentColor: ""
              property string mode: "all"
              property int size: 3
            }
          }
        }
        """;

    [Fact]
    public void A_string_property_keeps_its_value_when_the_file_has_another_type()
    {
        using var shell = new TestShell(
            ("shell.qml", Settings),
            ("settings.toml", "accentColor = 0x0078D4\nmode = [\"main\"]\nsize = 4\n"));
        var root = shell.Load();

        Assert.Equal("", shell.Eval(root, "settings.accentColor"));
        Assert.Equal("all", shell.Eval(root, "settings.mode"));
        Assert.Equal(4.0, shell.Eval(root, "settings.size"));
    }

    [Fact]
    public void Another_change_does_not_rewrite_a_value_of_another_type()
    {
        using var shell = new TestShell(
            ("shell.qml", Settings),
            ("settings.toml", "accentColor = 0x0078D4\nsize = 4\n"));
        var root = shell.Load();

        shell.Eval(root, "settings.size = 5");
        shell.Loop.Process();

        var text = File.ReadAllText(Path.Combine(shell.Directory, "settings.toml")).ReplaceLineEndings("\n");
        Assert.Contains("accentColor = 0x0078D4\n", text, StringComparison.Ordinal);
        Assert.Contains("size = 5\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Setting_the_property_replaces_the_value_of_another_type()
    {
        using var shell = new TestShell(
            ("shell.qml", Settings),
            ("settings.toml", "accentColor = 0x0078D4\n"));
        var root = shell.Load();

        shell.Eval(root, "settings.accentColor = '#0078d4'");
        shell.Loop.Process();

        var text = File.ReadAllText(Path.Combine(shell.Directory, "settings.toml")).ReplaceLineEndings("\n");
        Assert.Contains("accentColor = \"#0078d4\"\n", text, StringComparison.Ordinal);
    }
}
