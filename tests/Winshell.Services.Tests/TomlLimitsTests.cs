using System.Text;

namespace Winshell.Services.Tests;

/// <summary>
/// The budgets for a TOML file. The numbers are the production limits; every refusal uses a small budget or a short
/// in-memory document, so the test never builds a large file.
/// </summary>
public sealed class TomlLimitsTests
{
    private sealed class Settings(string path, IEventLoop loop, Log log) : TomlFile(path, loop, log)
    {
        public string Name { get; set => Set(ref field, value); } = "default";
    }

    private static string Dotted(int segments) => string.Join('.', Enumerable.Repeat("a", segments)) + " = 1";

    private static string Nested(int depth) => "a = " + new string('[', depth) + "1" + new string(']', depth);

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

        const string Preferences = """
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
        Assert.True(TomlDocument.TryParse("preferences.toml", Preferences, out var document, out var error), error);
        Assert.Equal("system", document.Values["colorScheme"]);
    }

    [Fact]
    public void Nesting_past_64_levels_is_refused()
    {
        Assert.True(TomlDocument.TryParse("deep.toml", Dotted(64), out _, out var error), error);
        Assert.False(TomlDocument.TryParse("deep.toml", Dotted(65), out _, out error));
        Assert.Contains("maximum depth of 64", error, StringComparison.Ordinal);

        Assert.True(TomlDocument.TryParse("arrays.toml", Nested(63), out _, out error), error);
        Assert.False(TomlDocument.TryParse("arrays.toml", Nested(64), out _, out error));
        Assert.Contains("maximum depth of 64", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_past_1_MiB_is_not_parsed()
    {
        var text = "# " + new string('x', DataLimits.MaxTextBytes);

        Assert.False(TomlDocument.TryParse("big.toml", text, out var document, out var error));
        Assert.Null(document);
        Assert.Contains("big.toml: file is ", error, StringComparison.Ordinal);
        Assert.Contains("above the limit of 1048576", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_past_the_limit_keeps_the_values_and_the_file()
    {
        using var fixture = new TomlFixture("name = \"kept\"");
        using var settings = new Settings(fixture.FilePath, fixture.Loop, fixture.Log);
        Assert.Equal("kept", settings.Name);

        var oversized = "name = \"replaced and longer\"\n"u8.ToArray();
        settings.MaxFileBytes = 8;
        File.WriteAllBytes(fixture.FilePath, oversized);

        Wait.Until(() => settings.Error.Length > 0, "the file past the limit", fixture.Loop.Process);
        Assert.Equal("kept", settings.Name);
        Assert.Contains("above the limit", settings.Error, StringComparison.Ordinal);
        Assert.Contains(fixture.Messages, m => m.Contains("above the limit", StringComparison.Ordinal));

        settings.Name = "next";
        fixture.Loop.Process();
        Assert.Equal(oversized, File.ReadAllBytes(fixture.FilePath));
        Assert.Contains(fixture.Messages, m => m.Contains("left unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void The_limit_counts_bytes_and_not_characters()
    {
        var accented = "name = \"é\"\n"u8.ToArray();
        using var fixture = new TomlFixture("name = \"kept\"");
        using var settings = new Settings(fixture.FilePath, fixture.Loop, fixture.Log);
        settings.MaxFileBytes = accented.Length - 1;

        File.WriteAllBytes(fixture.FilePath, accented);

        Wait.Until(() => settings.Error.Length > 0, "the file past the limit", fixture.Loop.Process);
        Assert.Equal("kept", settings.Name);
        Assert.Contains("above the limit", settings.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_back_under_the_limit_clears_the_error()
    {
        using var fixture = new TomlFixture("name = \"kept\"");
        using var settings = new Settings(fixture.FilePath, fixture.Loop, fixture.Log);
        settings.MaxFileBytes = 8;
        File.WriteAllText(fixture.FilePath, "name = \"replaced and longer\"\n");
        Wait.Until(() => settings.Error.Length > 0, "the file past the limit", fixture.Loop.Process);

        settings.MaxFileBytes = DataLimits.MaxTextBytes;
        File.WriteAllText(fixture.FilePath, "name = \"kept\"");

        Wait.Until(() => settings.Error.Length == 0, "the file back under the limit", fixture.Loop.Process);
    }

    [Fact]
    public void A_file_nested_too_deep_keeps_the_values_and_the_file()
    {
        using var fixture = new TomlFixture("name = \"kept\"");
        using var settings = new Settings(fixture.FilePath, fixture.Loop, fixture.Log);
        var bomb = Encoding.UTF8.GetBytes("bomb = " + new string('[', 64) + "1" + new string(']', 64) + "\n");
        Assert.InRange(bomb.Length, 1, 4096);

        File.WriteAllBytes(fixture.FilePath, bomb);

        Wait.Until(() => settings.Error.Length > 0, "the file nested too deep", fixture.Loop.Process);
        Assert.Equal("kept", settings.Name);
        Assert.Contains("maximum depth of 64", settings.Error, StringComparison.Ordinal);

        settings.Name = "next";
        fixture.Loop.Process();
        Assert.Equal(bomb, File.ReadAllBytes(fixture.FilePath));
    }
}
