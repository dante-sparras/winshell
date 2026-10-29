namespace Winshell.Runtime.Tests;

public class TomlAdapterTests
{
    private const string Settings = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias view: view
          property alias settings: adapter
          FileView {
            id: view
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            blockWrites: true
            printErrors: false
            onAdapterUpdated: writeAdapter()
            TomlAdapter {
              id: adapter
              comments: ({ name: "Who this is.", tiles: "The tiles, in order.\nOne per key." })
              property string name: "default"
              property bool on: true
              property int size: 3
              property real scale: 1.5
              property var tiles: ["wifi"]
              property var hidden: []
            }
          }
        }
        """;

    private static string Read(TestShell shell) => File.ReadAllText(Path.Combine(shell.Directory, "settings.toml")).ReplaceLineEndings("\n");

    [Fact]
    public void A_new_file_has_every_value_with_its_comment_above_it()
    {
        using var shell = new TestShell(("shell.qml", Settings));
        var root = shell.Load();
        shell.Eval(root, "view.writeAdapter()");
        shell.Loop.Process();

        Assert.Equal("""
            # Who this is.
            name = "default"

            on = true

            size = 3

            scale = 1.5

            # The tiles, in order.
            # One per key.
            tiles = ["wifi"]

            hidden = []

            """.ReplaceLineEndings("\n"), Read(shell));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Values_are_read_from_the_file_and_a_key_it_does_not_have_keeps_its_default()
    {
        using var shell = new TestShell(
            ("shell.qml", Settings),
            ("settings.toml", """
                name = 'from "file"'
                size = 7
                hidden = [{ id = "Contoso.Chat", name = "Chat" }, { id = "Contoso.Mail" }]
                unknown = 1
                """));
        var root = shell.Load();
        Assert.Equal("from \"file\"", shell.Eval(root, "settings.name"));
        Assert.Equal(7.0, shell.Eval(root, "settings.size"));
        Assert.Equal(true, shell.Eval(root, "settings.on"));
        Assert.Equal("Contoso.Chat,Chat|Contoso.Mail,", shell.Eval(root, "settings.hidden.map(a => a.id + ',' + (a.name ?? '')).join('|')"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Writing_changes_only_the_values_and_keeps_what_the_user_wrote()
    {
        using var shell = new TestShell(
            ("shell.qml", Settings),
            ("settings.toml", """
                # My settings.
                name = "mine" # set by hand

                # Not Winshell's.
                unknown = 1
                size = 3

                [other]
                kept = true
                """));
        var root = shell.Load();
        shell.Eval(root, "settings.name = 'changed \\\\ \"quoted\"'");
        shell.Eval(root, "settings.hidden = [{ id: 'Contoso.Chat', name: 'Chat' }]");
        shell.Loop.Process();

        Assert.Equal("""
            # My settings.
            name = "changed \\ \"quoted\"" # set by hand

            # Not Winshell's.
            unknown = 1
            size = 3

            on = true

            scale = 1.5

            # The tiles, in order.
            # One per key.
            tiles = ["wifi"]

            hidden = [{ id = "Contoso.Chat", name = "Chat" }]

            [other]
            kept = true
            """.ReplaceLineEndings("\n"), Read(shell));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_key_added_to_a_file_without_a_last_new_line_starts_on_its_own_line()
    {
        using var shell = new TestShell(("shell.qml", Settings), ("settings.toml", "size = 3"));
        var root = shell.Load();
        shell.Eval(root, "settings.size = 4");
        shell.Loop.Process();

        var text = Read(shell);
        Assert.StartsWith("size = 4\n\n# Who this is.\nname = \"default\"\n", text, StringComparison.Ordinal);
        Assert.Equal(4.0, shell.Eval(shell.Load(), "settings.size"));
    }

    [Fact]
    public void A_null_value_takes_its_key_out_and_the_comments_around_it_stay()
    {
        using var shell = new TestShell(
            ("shell.qml", Settings.Replace("property var hidden: []", "property var hidden: []\nproperty var gone: null", StringComparison.Ordinal)),
            ("settings.toml", """
                size = 3
                # Above gone.
                gone = 1
                # Above name.
                name = "kept"
                """));
        var root = shell.Load();
        shell.Eval(root, "settings.gone = null");
        shell.Loop.Process();

        Assert.StartsWith("size = 3\n# Above gone.\n# Above name.\nname = \"kept\"\n", Read(shell), StringComparison.Ordinal);
        Assert.DoesNotContain("gone =", Read(shell), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_does_not_parse_keeps_the_values_in_use_and_warns()
    {
        using var shell = new TestShell(("shell.qml", Settings), ("settings.toml", "name = \"broken"));
        var root = shell.Load();
        Assert.Equal("default", shell.Eval(root, "settings.name"));
        Assert.Contains(shell.Messages, m => m.Contains("TomlAdapter", StringComparison.Ordinal) && m.Contains("settings.toml", StringComparison.Ordinal));
    }
}
