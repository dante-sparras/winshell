namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>A hand-edited preferences.toml is held to the values Winshell Settings offers.</summary>
[Collection(ThemeCollection.Name)]
public class PreferencesCheckTests
{
    // Preferences reads the file once the test has written it.
    private const string View = """
        import Winshell
        import QtQuick
        Item {
          property bool ready: false
          property string backdrop: Preferences.backdrop
          property string startHotkey: Preferences.startHotkey
          property string settingsHotkey: Preferences.settingsHotkey
          onReadyChanged: Preferences.path = Shell.shellDir + "/preferences.toml"
        }
        """;

    [Fact]
    public void A_value_settings_would_not_offer_is_reported_and_not_used()
    {
        using var shell = Open("backdrop = \"Mica\"\nstartHotkey = \"K\"\nsettingsHotkey = \"Shift+S\"\n");

        Assert.Equal("acrylic", shell.Root.ReadProperty("backdrop"));
        // A bare key would be taken from every app on the PC.
        Assert.Equal("Win", shell.Root.ReadProperty("startHotkey"));
        Assert.Equal("", shell.Root.ReadProperty("settingsHotkey"));
        Assert.Contains(shell.Messages, m => m.Contains("backdrop is not \"acrylic\" or \"mica\"", StringComparison.Ordinal));
        Assert.Contains(shell.Messages, m => m.Contains("startHotkey is refused: Start it with Win, Ctrl or Alt, so K still types in other apps", StringComparison.Ordinal));
        Assert.Contains(shell.Messages, m => m.Contains("settingsHotkey is refused", StringComparison.Ordinal));
    }

    [Fact]
    public void Values_settings_offers_are_used_quietly()
    {
        using var shell = Open("backdrop = \"mica\"\nstartHotkey = \"Ctrl+Alt+S\"\nsettingsHotkey = \"\"\n");

        Assert.Equal("mica", shell.Root.ReadProperty("backdrop"));
        Assert.Equal("Ctrl+Alt+S", shell.Root.ReadProperty("startHotkey"));
        Assert.Empty(shell.Messages);
    }

    private static DefaultShellHarness Open(string preferences)
    {
        var shell = new DefaultShellHarness(View);
        File.WriteAllText(Path.Combine(shell.Directory, "preferences.toml"), preferences);
        shell.Root.WriteProperty("ready", true);
        shell.Settle();
        return shell;
    }
}
