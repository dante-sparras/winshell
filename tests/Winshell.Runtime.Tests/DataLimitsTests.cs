using System.Text;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// Budgets for a file read and a TOML parse. The numbers are the production limits; every refusal uses a small
/// budget or a few hundred bytes, so the test never builds a large file.
/// </summary>
public class DataLimitsTests
{
    private const string Settings = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias view: view
          property alias settings: adapter
          property int failure: -1
          FileView {
            id: view
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            blockWrites: true
            printErrors: true
            onLoadFailed: error => failure = error
            onAdapterUpdated: writeAdapter()
            TomlAdapter {
              id: adapter
              property string name: "default"
              property var hidden: []
            }
          }
        }
        """;

    [Fact]
    public void The_budgets_sit_above_a_preferences_file()
    {
        Assert.Equal(1024 * 1024, DataLimits.MaxFileBytes);
        Assert.Equal(100_000, DataLimits.MaxNodes);
        Assert.Equal(64, DataLimits.MaxDepth);
        Assert.Null(DataLimits.FileBytes(DataLimits.MaxFileBytes, DataLimits.MaxFileBytes));
        Assert.Null(DataLimits.FileBytes(-1, DataLimits.MaxFileBytes));
        Assert.Contains("above the limit of ", DataLimits.FileBytes(DataLimits.MaxFileBytes + 1, DataLimits.MaxFileBytes), StringComparison.Ordinal);

        // A dozen keys, and the deepest value the default shell writes: one array of one inline table.
        const string preferences = """
            colorScheme = "system"
            accentColor = ""
            backdrop = "acrylic"
            taskbarCentered = true
            taskbarAutoHide = false
            taskbarAllScreens = true
            taskbarAppsOn = "all"
            startRecommended = true
            hiddenNotificationApps = [{ id = "Contoso.Chat", name = "Chat" }]
            startHotkey = "Win"
            settingsHotkey = ""
            autoUpdate = true
            """;
        Assert.Null(DataLimits.Toml(preferences, DataLimits.MaxFileBytes, DataLimits.MaxNodes, DataLimits.MaxDepth));
    }

    [Fact]
    public void Strings_comments_and_quoted_keys_are_not_structure()
    {
        Accept("a = \"[[[not deep]]]\"", maxNodes: 1);
        Accept("a = 1 # [[[", maxNodes: 1);
        Accept("a = \"\"\"\n[[\n\"\"\"", maxNodes: 1);
        Accept("\"a.b.c\" = 1", maxNodes: 1);
        Accept("scale = 1.5", maxNodes: 1);
    }

    [Fact]
    public void Nesting_and_value_count_follow_the_budget()
    {
        RefuseDepth("a = [1]", 0);
        Accept("a = [1]", maxDepth: 1);
        RefuseDepth("a = [[1]]", 1);
        Accept("a = [[1]]", maxDepth: 2);
        RefuseDepth("a = { b = 1 }", 0);
        Accept("a = { b = 1 }", maxDepth: 1);
        RefuseDepth("a.b = 1", 0);
        Accept("a.b = 1", maxDepth: 1);
        RefuseDepth("[a]", 0);
        Accept("[a]", maxDepth: 1);
        RefuseDepth("[a.b]", 1);
        Accept("[a.b]", maxDepth: 2);
        RefuseDepth("[[a]]", 1);
        Accept("[[a]]", maxDepth: 2);
        RefuseDepth("a.b = { c = 1 }", 1);
        Accept("a.b = { c = 1 }", maxDepth: 2);
        Accept("hidden = [{ id = \"a\", name = \"b\" }]", maxDepth: 2);

        var two = DataLimits.Toml("a = 1\nb = 2", 1000, maxNodes: 1, maxDepth: 64);
        Assert.Contains("has more than", two, StringComparison.Ordinal);
        Assert.Null(DataLimits.Toml("a = 1\nb = 2", 1000, 2, 64));

        var tooDeep = "a = " + new string('[', 65) + "1" + new string(']', 65);
        Assert.Contains(
            "nested deeper than 64",
            DataLimits.Toml(tooDeep, DataLimits.MaxFileBytes, DataLimits.MaxNodes, DataLimits.MaxDepth),
            StringComparison.Ordinal);
        var atLimit = "a = " + new string('[', 64) + "1" + new string(']', 64);
        Assert.Null(DataLimits.Toml(atLimit, DataLimits.MaxFileBytes, DataLimits.MaxNodes, DataLimits.MaxDepth));

        Assert.Contains("above the limit of 4", DataLimits.Toml("hello", 4, 100, 64), StringComparison.Ordinal);
        Assert.Contains("above the limit of 1", DataLimits.Toml("é", 1, 100, 64), StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_read_keeps_the_loaded_values_and_the_file()
    {
        using var shell = new TestShell(("shell.qml", Settings), ("settings.toml", "name = \"kept\""));
        var root = shell.Load();
        var view = (FileView)root.ReadProperty("view")!;
        var settings = (TomlAdapter)root.ReadProperty("settings")!;
        var path = Path.Combine(shell.Directory, "settings.toml");
        Assert.Equal("kept", shell.Eval(root, "settings.name"));

        var oversized = "name = \"replaced and longer\"\n"u8.ToArray();
        File.WriteAllBytes(path, oversized);
        view.MaxFileBytes = 8;
        view.Reload();
        Assert.Equal("kept", shell.Eval(root, "settings.name"));
        Assert.Equal("name = \"kept\"", shell.Eval(root, "view.text()"));
        Assert.Equal(1.0, shell.Eval(root, "failure"));
        Assert.Contains("above the limit", settings.Error, StringComparison.Ordinal);
        Assert.Contains("above the limit", string.Join('\n', shell.Messages), StringComparison.Ordinal);
        shell.Eval(root, "settings.name = 'next'");
        Assert.Equal(oversized, File.ReadAllBytes(path));

        view.MaxFileBytes = DataLimits.MaxFileBytes;
        File.WriteAllText(path, "name = \"kept\"");
        view.Reload();
        settings.MaxNodes = 1;
        var wide = "name = \"other\"\nextra = 1\n"u8.ToArray();
        File.WriteAllBytes(path, wide);
        view.Reload();
        Assert.Equal("kept", shell.Eval(root, "settings.name"));
        Assert.Contains("has more than", settings.Error, StringComparison.Ordinal);
        shell.Eval(root, "settings.name = 'next'");
        Assert.Equal(wide, File.ReadAllBytes(path));

        settings.MaxNodes = DataLimits.MaxNodes;
        settings.MaxDepth = DataLimits.MaxDepth;
        File.WriteAllText(path, "name = \"kept\"");
        view.Reload();
        var bomb = Encoding.UTF8.GetBytes("name = \"other\"\nbomb = " + new string('[', 70) + "1" + new string(']', 70) + "\n");
        Assert.InRange(bomb.Length, 1, 4096);
        File.WriteAllBytes(path, bomb);
        view.Reload();
        Assert.Equal("kept", shell.Eval(root, "settings.name"));
        Assert.Contains("nested deeper than 64", settings.Error, StringComparison.Ordinal);
        shell.Eval(root, "settings.name = 'next'");
        Assert.Equal(bomb, File.ReadAllBytes(path));

        settings.MaxDepth = 2;
        settings.MaxNodes = 20;
        File.WriteAllText(path, "name = \"kept\"\nhidden = [{ id = \"a\", name = \"b\" }]\n");
        view.Reload();
        Assert.True(string.IsNullOrEmpty(settings.Error), settings.Error);
        Assert.Equal("kept", shell.Eval(root, "settings.name"));
        Assert.Equal("a,b", shell.Eval(root, "settings.hidden.map(a => a.id + ',' + (a.name ?? '')).join('|')"));
    }

    private static void Accept(string text, int maxDepth = 0, int maxNodes = 20) =>
        Assert.Null(DataLimits.Toml(text, 10_000, maxNodes, maxDepth));

    private static void RefuseDepth(string text, int maxDepth)
    {
        var why = DataLimits.Toml(text, 10_000, 10_000, maxDepth);
        Assert.Contains("nested deeper than " + maxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture), why, StringComparison.Ordinal);
    }
}
