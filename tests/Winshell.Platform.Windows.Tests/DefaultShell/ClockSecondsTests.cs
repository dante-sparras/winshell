using System.Text.RegularExpressions;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>The taskbar clock shows seconds only when <c>clockSeconds</c> is on.</summary>
[Collection(ThemeCollection.Name)]
public partial class ClockSecondsTests
{
    private const string Qml = """
        import Winshell
        TaskbarClock {
          property bool ready: false
          onReadyChanged: Preferences.path = Shell.shellDir + "/preferences.toml"
        }
        """;

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();

    private static int TimeParts(DefaultShellHarness shell) =>
        Number().Count(((string)shell.Find("clockButton").ReadProperty("text")!).Split('\n')[0]);

    private static DefaultShellHarness Clock(bool seconds)
    {
        var shell = new DefaultShellHarness(Qml);
        File.WriteAllText(Path.Combine(shell.Directory, "preferences.toml"), $"clockSeconds = {(seconds ? "true" : "false")}\n");
        shell.Root.WriteProperty("ready", true);
        shell.Settle();
        return shell;
    }

    [Fact]
    public void The_clock_has_no_seconds_by_default()
    {
        using var shell = Clock(seconds: false);

        Assert.Equal(2, TimeParts(shell));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void The_preference_adds_seconds()
    {
        using var shell = Clock(seconds: true);

        Assert.Equal(3, TimeParts(shell));
        Assert.Empty(shell.Messages);
    }
}
