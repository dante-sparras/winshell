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

    [Fact]
    public void An_array_of_tables_is_not_written_a_second_time()
    {
        using var shell = new TestShell(("shell.qml", Settings));
        var path = FileOf(shell);
        File.WriteAllText(path, """
            # apps
            [[hidden]]
            id = "Contoso.Chat" # chat
            name = "Chat"

            [[hidden]]
            id = "Contoso.Mail"

            [other]
            kept = true
            """.ReplaceLineEndings("\n"));
        var root = shell.Load();
        shell.Eval(root, "settings.name = 'renamed'");
        shell.Loop.Process();

        var text = File.ReadAllText(path);
        Assert.False(Tomlyn.Parsing.SyntaxParser.Parse(text).HasErrors);
        Assert.Equal(2, Count(text, "[[hidden]]"));
        Assert.DoesNotContain("hidden =", text, StringComparison.Ordinal);
        Assert.Contains("# chat", text, StringComparison.Ordinal);
        Assert.Contains("[other]", text, StringComparison.Ordinal);
        Assert.Contains("kept = true", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_table_row_keeps_its_comment_and_unknown_key()
    {
        using var shell = new TestShell(("shell.qml", Settings));
        var path = FileOf(shell);
        File.WriteAllText(path, """
            [[hidden]]
            id = "Contoso.Chat" # chat
            name = "Chat"
            extra = 1

            [[hidden]]
            id = "Contoso.Mail"

            [other]
            kept = true
            """.ReplaceLineEndings("\n"));
        var root = shell.Load();
        shell.Eval(root, "settings.hidden = [{ id: 'Contoso.Chat', name: 'Renamed', extra: 1 }]");
        shell.Loop.Process();

        var text = File.ReadAllText(path);
        Assert.False(Tomlyn.Parsing.SyntaxParser.Parse(text).HasErrors);
        Assert.Equal(1, Count(text, "[[hidden]]"));
        Assert.Contains("id = \"Contoso.Chat\" # chat", text, StringComparison.Ordinal);
        Assert.Contains("name = \"Renamed\"", text, StringComparison.Ordinal);
        Assert.Contains("extra = 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden =", text, StringComparison.Ordinal);
        Assert.Contains("[other]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unchanged_value_keeps_its_spelling_and_item_comment()
    {
        const string Original = """
            name = 'mine'
            hidden = [
              { id = "Contoso.Chat", name = "Chat" }, # chat
            ]
            scale = 1.0
            when = 1979-05-27
            hex = 0x10
            grouped = 1_000
            size = 3
            """;
        using var shell = new TestShell(("shell.qml", Spellings));
        var path = FileOf(shell);
        var original = Original.ReplaceLineEndings("\n");
        File.WriteAllText(path, original);
        var root = shell.Load();
        shell.Eval(root, "settings.size = 4");
        shell.Loop.Process();

        var text = File.ReadAllText(path);
        Assert.Equal(original.Replace("size = 3", "size = 4", StringComparison.Ordinal), text);
    }

    [Fact]
    public void A_changed_array_item_keeps_its_comment()
    {
        using var shell = new TestShell(("shell.qml", Spellings));
        var path = FileOf(shell);
        File.WriteAllText(path, """
            hidden = [
              { id = "Contoso.Chat", name = "Chat" }, # chat
            ]
            size = 3
            """.ReplaceLineEndings("\n"));
        var root = shell.Load();
        shell.Eval(root, "settings.hidden = [{ id: 'Contoso.Chat', name: 'Renamed' }]");
        shell.Loop.Process();

        var text = File.ReadAllText(path);
        Assert.Contains("name = \"Renamed\"", text, StringComparison.Ordinal);
        Assert.Contains("# chat", text, StringComparison.Ordinal);
        Assert.Contains("id = \"Contoso.Chat\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bool_property_leaves_a_string_array_or_table_unchanged()
    {
        foreach (var value in new[] { "\"false\"", "[false]", "{ on = false }" })
        {
            using var shell = new TestShell(("shell.qml", BoolSettings));
            var path = FileOf(shell);
            File.WriteAllText(path, $"taskbarAutoHide = {value}\nname = \"mine\"\n");
            var root = shell.Load();
            Assert.Equal(false, shell.Eval(root, "settings.taskbarAutoHide"));
            Assert.Contains(shell.Messages, m => m.Contains("taskbarAutoHide", StringComparison.Ordinal));
            shell.Eval(root, "settings.name = 'next'");
            shell.Loop.Process();
            Assert.Contains($"taskbarAutoHide = {value}", File.ReadAllText(path), StringComparison.Ordinal);
        }

        using var assigned = new TestShell(("shell.qml", BoolSettings));
        var assignedPath = FileOf(assigned);
        File.WriteAllText(assignedPath, "taskbarAutoHide = \"false\"\nname = \"mine\"\n");
        var assignedRoot = assigned.Load();
        assigned.Eval(assignedRoot, "settings.taskbarAutoHide = true");
        assigned.Loop.Process();
        var written = File.ReadAllText(assignedPath);
        Assert.Contains("taskbarAutoHide = true", written, StringComparison.Ordinal);
        Assert.DoesNotContain("taskbarAutoHide = \"false\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_does_not_parse_is_left_unchanged_by_a_write()
    {
        using var shell = new TestShell(("shell.qml", Settings), ("settings.toml", "name = \"broken"));
        var root = shell.Load();
        var path = FileOf(shell);
        var before = File.ReadAllBytes(path);
        shell.Eval(root, "view.writeAdapter()");
        shell.Loop.Process();
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Contains(shell.Messages, m => m.Contains("left unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void A_torn_buffer_is_written_from_the_copy_that_parses()
    {
        using var shell = new TestShell(("shell.qml", Settings), ("settings.toml", "name = \"broken"));
        var root = shell.Load();
        var path = FileOf(shell);
        File.WriteAllText(path, "name = \"disk\" # keep\n\n[other]\nkept = true\n");
        shell.Eval(root, "settings.on = false");
        shell.Loop.Process();

        var text = File.ReadAllText(path);
        Assert.False(Tomlyn.Parsing.SyntaxParser.Parse(text).HasErrors);
        Assert.Contains("name = \"default\" # keep", text, StringComparison.Ordinal);
        Assert.Contains("[other]", text, StringComparison.Ordinal);
        Assert.Contains("kept = true", text, StringComparison.Ordinal);
        Assert.Contains("on = false", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_added_to_a_crlf_file_uses_crlf()
    {
        using var shell = new TestShell(("shell.qml", Settings));
        var path = FileOf(shell);
        File.WriteAllBytes(path, "name = \"mine\" # hand\r\n"u8.ToArray());
        var root = shell.Load();
        shell.Eval(root, "settings.name = 'yours'");
        shell.Loop.Process();

        var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("name = \"yours\" # hand\r\n", text, StringComparison.Ordinal);
        Assert.Contains("on = true\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_save_50ms_after_a_write_becomes_the_properties()
    {
        using var shell = new TestShell(("shell.qml", Watching), ("settings.toml", "name = \"file\"\n"));
        var root = shell.Load();
        shell.Eval(root, "settings.on = false");
        shell.Loop.Process();
        Thread.Sleep(50);
        File.WriteAllText(FileOf(shell), "name = \"by hand\"\n");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (shell.Eval(root, "settings.name") is not "by hand")
        {
            Assert.True(DateTime.UtcNow < until, "The file written 50ms later was not read.");
            Thread.Sleep(20);
            shell.Loop.Process();
        }
    }

    [Fact]
    public void A_watched_save_is_still_in_the_file_after_the_watcher_runs()
    {
        using var shell = new TestShell(("shell.qml", Watching), ("settings.toml", "name = \"mine\"\n"));
        var root = shell.Load();
        shell.Eval(root, "settings.name = 'next'");
        shell.Loop.Process();
        // The watch event is delivered on another thread. Let it run, then read on the loop.
        Thread.Sleep(200);
        shell.Loop.Process();

        Assert.Contains("name = \"next\"", File.ReadAllText(FileOf(shell)), StringComparison.Ordinal);
        Assert.Equal("next", shell.Eval(root, "settings.name"));
    }

    [Fact]
    public void A_leading_comment_stays_above_the_first_key_added()
    {
        using var shell = new TestShell(("shell.qml", Settings));
        var path = FileOf(shell);
        File.WriteAllText(path, "# header\n\n[other]\nkept = true\n");
        var root = shell.Load();
        shell.Eval(root, "view.writeAdapter()");
        shell.Loop.Process();
        var text = File.ReadAllText(path);
        Assert.True(text.IndexOf("# header", StringComparison.Ordinal) < text.IndexOf("name =", StringComparison.Ordinal));
        Assert.Contains("[other]", text, StringComparison.Ordinal);

        File.WriteAllText(path, "# header comment\n");
        shell.Eval(root, "view.reload()");
        shell.Loop.Process();
        shell.Eval(root, "view.writeAdapter()");
        shell.Loop.Process();
        Assert.StartsWith("# header comment\n", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_order_mark_is_kept()
    {
        using var shell = new TestShell(("shell.qml", Settings));
        var path = FileOf(shell);
        var body = "name = \"mine\"\n"u8.ToArray();
        var bytes = new byte[body.Length + 3];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        body.CopyTo(bytes, 3);
        File.WriteAllBytes(path, bytes);
        var root = shell.Load();
        shell.Eval(root, "settings.name = 'next'");
        shell.Loop.Process();

        var written = File.ReadAllBytes(path);
        Assert.Equal((byte)0xEF, written[0]);
        Assert.Equal((byte)0xBB, written[1]);
        Assert.Equal((byte)0xBF, written[2]);
        Assert.Contains("name = \"next\"", System.Text.Encoding.UTF8.GetString(written[3..]), StringComparison.Ordinal);
    }

    [Fact]
    public void An_integer_past_2_to_the_53_is_not_rounded()
    {
        using var shell = new TestShell(("shell.qml", BigInteger));
        var path = FileOf(shell);
        File.WriteAllText(path, "big = 9223372036854775807\nname = \"mine\"\n");
        var root = shell.Load();
        Assert.Equal(0.0, shell.Eval(root, "settings.big"));
        Assert.Contains(shell.Messages, m => m.Contains("big", StringComparison.Ordinal));
        shell.Eval(root, "settings.name = 'next'");
        shell.Loop.Process();
        Assert.Contains("big = 9223372036854775807", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.DoesNotContain("E+", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_quoted_key_is_the_same_property()
    {
        using var shell = new TestShell(("shell.qml", Settings));
        var path = FileOf(shell);
        File.WriteAllText(path, "\"name\" = \"quoted\" # q\n'size' = 9\n");
        var root = shell.Load();
        Assert.Equal("quoted", shell.Eval(root, "settings.name"));
        Assert.Equal(9.0, shell.Eval(root, "settings.size"));
        shell.Eval(root, "settings.name = 'next'");
        shell.Loop.Process();

        var text = File.ReadAllText(path);
        Assert.Contains("\"name\" = \"next\" # q", text, StringComparison.Ordinal);
        Assert.Contains("'size' = 9", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "name"));
    }

    [Fact]
    public void A_dotted_key_defines_the_property()
    {
        using var shell = new TestShell(("shell.qml", Dotted));
        var path = FileOf(shell);
        File.WriteAllText(path, "taskbar.centered = false # stay\nname = \"mine\"\n");
        var root = shell.Load();
        Assert.Equal(false, shell.Eval(root, "settings.taskbar.centered"));
        shell.Eval(root, "settings.name = 'next'");
        shell.Loop.Process();
        var kept = File.ReadAllText(path);
        Assert.Contains("taskbar.centered = false # stay", kept, StringComparison.Ordinal);
        Assert.DoesNotContain("taskbar =", kept, StringComparison.Ordinal);

        shell.Eval(root, "settings.taskbar = ({ centered: true })");
        shell.Loop.Process();
        var changed = File.ReadAllText(path);
        Assert.Contains("taskbar.centered = true # stay", changed, StringComparison.Ordinal);
        Assert.DoesNotContain("taskbar =", changed, StringComparison.Ordinal);
        Assert.False(Tomlyn.Parsing.SyntaxParser.Parse(changed).HasErrors);
    }

    [Fact]
    public void One_bad_value_does_not_stop_the_rest_of_the_load()
    {
        using var shell = new TestShell(("shell.qml", Settings));
        var path = FileOf(shell);
        File.WriteAllText(path, "size = \"x\"\nname = \"kept\"\n");
        var root = shell.Load();
        Assert.Equal("kept", shell.Eval(root, "settings.name"));
        Assert.Equal(3.0, shell.Eval(root, "settings.size"));
        Assert.Contains(shell.Messages, m => m.Contains("size", StringComparison.Ordinal));

        shell.Eval(root, "settings.name = 'next'");
        shell.Loop.Process();
        Assert.Contains("size = \"x\"", File.ReadAllText(path), StringComparison.Ordinal);

        shell.Eval(root, "settings.size = 8");
        shell.Loop.Process();
        var text = File.ReadAllText(path);
        Assert.Contains("size = 8", text, StringComparison.Ordinal);
        Assert.DoesNotContain("size = \"x\"", text, StringComparison.Ordinal);
    }

    private const string Spellings = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias settings: adapter
          FileView {
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            blockWrites: true
            printErrors: false
            onAdapterUpdated: writeAdapter()
            TomlAdapter {
              id: adapter
              property string name: "default"
              property var hidden: []
              property real scale: 1
              property var when
              property int hex: 0
              property int grouped: 0
              property int size: 3
            }
          }
        }
        """;

    private const string BoolSettings = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias settings: adapter
          FileView {
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            blockWrites: true
            printErrors: false
            onAdapterUpdated: writeAdapter()
            TomlAdapter {
              id: adapter
              property bool taskbarAutoHide: false
              property string name: "default"
            }
          }
        }
        """;

    private const string Watching = """
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

    private const string BigInteger = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias settings: adapter
          FileView {
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            blockWrites: true
            printErrors: false
            onAdapterUpdated: writeAdapter()
            TomlAdapter {
              id: adapter
              property var big: 0
              property string name: "default"
            }
          }
        }
        """;

    private const string Dotted = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias settings: adapter
          FileView {
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            blockWrites: true
            printErrors: false
            onAdapterUpdated: writeAdapter()
            TomlAdapter {
              id: adapter
              property var taskbar: ({ centered: false })
              property string name: "default"
            }
          }
        }
        """;

    private static string FileOf(TestShell shell) => Path.Combine(shell.Directory, "settings.toml");

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
