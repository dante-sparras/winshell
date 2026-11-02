namespace Winshell.DefaultShell.Tests;

public sealed class CalendarPreferencesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-calendar-").FullName;

    private string Toml => Path.Combine(_folder, "preferences.toml");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_week_number_choice_is_saved_and_a_hand_edit_reaches_the_calendar()
    {
        var loop = new ManualEventLoop();
        using var preferences = new Preferences(Toml, loop);
        Assert.False(preferences.CalendarWeekNumbers);

        preferences.CalendarWeekNumbers = true;
        loop.Process();

        var saved = File.ReadAllText(Toml);
        Assert.Contains("calendarWeekNumbers = true", saved, StringComparison.Ordinal);

        using var reopened = new Preferences(Toml, new ManualEventLoop());
        Assert.True(reopened.CalendarWeekNumbers);

        File.WriteAllText(Toml, "calendarWeekNumbers = false\n");
        Wait.Until(() => !preferences.CalendarWeekNumbers, "the hand edit", loop.Process);
        Assert.False(preferences.CalendarWeekNumbers);
    }
}
