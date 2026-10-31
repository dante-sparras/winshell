namespace Winshell.Runtime.Tests;

/// <summary><c>TomlAdapter.check</c> refuses a value read from the file, which is then left as the file has it.</summary>
public class TomlAdapterCheckTests
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
              check: (key, value) => key === "backdrop" && !["acrylic", "mica"].includes(value) ? "is not \"acrylic\" or \"mica\"" : ""
              property string backdrop: "acrylic"
              property int size: 3
            }
          }
        }
        """;

    [Fact]
    public void A_refused_value_keeps_the_one_in_use_and_names_the_key()
    {
        using var shell = new TestShell(("shell.qml", Settings), ("settings.toml", "backdrop = \"Mica\"\nsize = 4\n"));
        var root = shell.Load();

        Assert.Equal("acrylic", shell.Eval(root, "settings.backdrop"));
        Assert.Equal(4.0, shell.Eval(root, "settings.size"));
        Assert.Contains(shell.Messages, m => m.Contains("backdrop is not \"acrylic\" or \"mica\", so it was left unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_it_takes_is_set_without_a_word()
    {
        using var shell = new TestShell(("shell.qml", Settings), ("settings.toml", "backdrop = \"mica\"\n"));
        var root = shell.Load();

        Assert.Equal("mica", shell.Eval(root, "settings.backdrop"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Another_change_does_not_rewrite_a_refused_value()
    {
        using var shell = new TestShell(("shell.qml", Settings), ("settings.toml", "backdrop = \"Mica\"\nsize = 4\n"));
        var root = shell.Load();

        shell.Eval(root, "settings.size = 5");
        shell.Loop.Process();

        var text = File.ReadAllText(Path.Combine(shell.Directory, "settings.toml")).ReplaceLineEndings("\n");
        Assert.Contains("backdrop = \"Mica\"\n", text, StringComparison.Ordinal);
        Assert.Contains("size = 5\n", text, StringComparison.Ordinal);
    }
}
