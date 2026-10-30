using Tomlyn;
using Tomlyn.Model;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

[Collection(ThemeCollection.Name)]
public class CalendarPreferencesTests
{
    private const string Qml = """
        import Winshell
        CalendarView {
          property bool ready: false
          onReadyChanged: Preferences.path = Shell.shellDir + "/preferences.toml"
        }
        """;

    [Fact]
    public void The_week_number_choice_is_saved_and_read_by_the_next_calendar()
    {
        string saved;
        using (var calendar = new DefaultShellHarness(Qml))
        {
            calendar.Root.WriteProperty("ready", true);
            calendar.Settle();
            Assert.False((bool)calendar.Root.ReadProperty("showWeekNumbers")!);
            var checkbox = DefaultShellHarness.FindAll(calendar.Root).OfType<CheckBox>().Single();
            calendar.Click(checkbox);
            Assert.True((bool)calendar.Root.ReadProperty("showWeekNumbers")!);
            saved = File.ReadAllText(Path.Combine(calendar.Directory, "preferences.toml"));
            Assert.True((bool)TomlSerializer.Deserialize<TomlTable>(saved)!["calendarWeekNumbers"]!);
            Assert.Empty(calendar.Messages);
        }

        using var reopened = new DefaultShellHarness(Qml);
        File.WriteAllText(Path.Combine(reopened.Directory, "preferences.toml"), saved);
        reopened.Root.WriteProperty("ready", true);
        reopened.Settle();
        Assert.True((bool)reopened.Root.ReadProperty("showWeekNumbers")!);
        Assert.True(DefaultShellHarness.FindAll(reopened.Root).OfType<CheckBox>().Single().Checked);
        Assert.Empty(reopened.Messages);
    }
}
