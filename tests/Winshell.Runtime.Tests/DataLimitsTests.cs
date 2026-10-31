using System.Text;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// Budgets for a file read and a TOML parse. The numbers are the production limits; every refusal uses a small
/// budget or a short in-memory document, so the test never builds a large file.
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
        Assert.Equal(64 * 1024 * 1024, DataLimits.MaxReadBytes);
        Assert.Equal(1024 * 1024, DataLimits.MaxTextBytes);
        Assert.Null(DataLimits.FileBytes(DataLimits.MaxTextBytes, DataLimits.MaxTextBytes));
        Assert.Null(DataLimits.FileBytes(-1, DataLimits.MaxTextBytes));
        Assert.Contains(
            "above the limit of ",
            DataLimits.FileBytes(DataLimits.MaxReadBytes + 1, DataLimits.MaxReadBytes),
            StringComparison.Ordinal);

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
        Assert.True(TomlAdapter.TryLoad("preferences.toml", preferences, out var table, out var error), error);
        Assert.Equal("system", table["colorScheme"]);

        Assert.True(TomlAdapter.TryLoad("deep.toml", Dotted(64), out _, out error), error);
        Assert.False(TomlAdapter.TryLoad("deep.toml", Dotted(65), out _, out error));
        Assert.Contains("maximum depth of 64", error, StringComparison.Ordinal);

        Assert.True(TomlAdapter.TryLoad("arrays.toml", Nested(63), out _, out error), error);
        Assert.False(TomlAdapter.TryLoad("arrays.toml", Nested(64), out _, out error));
        Assert.Contains("maximum depth of 64", error, StringComparison.Ordinal);
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

        view.MaxFileBytes = DataLimits.MaxReadBytes;
        File.WriteAllText(path, "name = \"kept\"");
        view.Reload();
        var accented = "name = \"é\"\n"u8.ToArray();
        settings.MaxFileBytes = accented.Length - 1;
        File.WriteAllBytes(path, accented);
        view.Reload();
        Assert.Equal("kept", shell.Eval(root, "settings.name"));
        Assert.Contains("above the limit", settings.Error, StringComparison.Ordinal);
        shell.Eval(root, "settings.name = 'next'");
        Assert.Equal(accented, File.ReadAllBytes(path));

        settings.MaxFileBytes = DataLimits.MaxTextBytes;
        File.WriteAllText(path, "name = \"kept\"");
        view.Reload();
        Assert.Equal("kept", shell.Eval(root, "settings.name"));
        settings.MaxDepth = 1;
        var nested = "name = \"other\"\nhidden = [{ id = \"a\", name = \"b\" }]\n"u8.ToArray();
        File.WriteAllBytes(path, nested);
        view.Reload();
        Assert.Equal("kept", shell.Eval(root, "settings.name"));
        Assert.Contains("nested deeper than 1", settings.Error, StringComparison.Ordinal);
        Assert.Equal(nested, File.ReadAllBytes(path));

        settings.MaxDepth = 2;
        view.Reload();
        Assert.True(string.IsNullOrEmpty(settings.Error), settings.Error);
        Assert.Equal("other", shell.Eval(root, "settings.name"));
        Assert.Equal("a,b", shell.Eval(root, "settings.hidden.map(a => a.id + ',' + (a.name ?? '')).join('|')"));

        settings.MaxDepth = 64;
        File.WriteAllText(path, "name = \"kept\"");
        view.Reload();
        var bomb = Encoding.UTF8.GetBytes("bomb = " + new string('[', 64) + "1" + new string(']', 64) + "\n");
        Assert.InRange(bomb.Length, 1, 4096);
        File.WriteAllBytes(path, bomb);
        view.Reload();
        Assert.Equal("kept", shell.Eval(root, "settings.name"));
        Assert.Contains("maximum depth of 64", settings.Error, StringComparison.Ordinal);
        shell.Eval(root, "settings.name = 'next'");
        Assert.Equal(bomb, File.ReadAllBytes(path));
    }

    private static string Dotted(int segments) => string.Join('.', Enumerable.Repeat("a", segments)) + " = 1";

    private static string Nested(int depth) => "a = " + new string('[', depth) + "1" + new string(']', depth);
}
