using System.Text;

namespace Winshell.Core.Tests;

/// <summary>
/// The budgets for a TOML file. The numbers are the production limits; a file past one is as large as the limit.
/// </summary>
public sealed class TomlLimitsTests
{
    private const int Megabyte = 1024 * 1024;

    private sealed class Settings(string path, IEventLoop loop, Log log) : TomlFile(path, loop, log)
    {
        public string Name
        {
            get;
            set => Set(ref field, value);
        } = "default";
    }

    private static string Dotted(int segments)
    {
        return string.Join('.', Enumerable.Repeat("a", segments)) + " = 1";
    }

    private static string Nested(int depth)
    {
        return "a = " + new string('[', depth) + "1" + new string(']', depth);
    }

    [Fact]
    public void The_budgets_sit_above_a_preferences_file()
    {
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
        var text = "# " + new string('x', Megabyte);

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

        var oversized = Encoding.UTF8.GetBytes("name = \"" + new string('x', Megabyte) + "\"\n");
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
        // Fewer characters than the limit, more bytes.
        var accented = Encoding.UTF8.GetBytes("name = \"" + new string('é', Megabyte / 2) + "\"\n");
        Assert.True(accented.Length > Megabyte);
        using var fixture = new TomlFixture("name = \"kept\"");
        using var settings = new Settings(fixture.FilePath, fixture.Loop, fixture.Log);

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
        File.WriteAllText(fixture.FilePath, "name = \"" + new string('x', Megabyte) + "\"\n");
        Wait.Until(() => settings.Error.Length > 0, "the file past the limit", fixture.Loop.Process);

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

    [Fact]
    public void Loading_a_file_names_what_went_wrong()
    {
        var dir = Directory.CreateTempSubdirectory("winshell-toml-load-");
        var missing = Path.Combine(dir.FullName, "missing.toml");
        var bad = Path.Combine(dir.FullName, "bad.toml");
        var big = Path.Combine(dir.FullName, "big.toml");
        var marked = Path.Combine(dir.FullName, "marked.toml");
        var plain = Path.Combine(dir.FullName, "plain.toml");
        try
        {
            Assert.Equal(TomlRead.Missing, TomlDocument.Load(missing).Kind);

            File.WriteAllText(bad, "name =\n");
            var invalid = TomlDocument.Load(bad);
            Assert.Equal(TomlRead.Invalid, invalid.Kind);
            Assert.Equal(1, invalid.Line);
            Assert.True(invalid.Column > 0);
            Assert.Contains($"{bad}[{invalid.Line}:{invalid.Column}]:", invalid.Describe(bad),
                StringComparison.Ordinal);

            File.WriteAllBytes(big, new byte[Megabyte + 1]);
            var tooLarge = TomlDocument.Load(big);
            Assert.Equal(TomlRead.TooLarge, tooLarge.Kind);
            Assert.Contains("above the limit of 1048576", tooLarge.Message, StringComparison.Ordinal);
            Assert.StartsWith($"{big}: file is ", tooLarge.Describe(big), StringComparison.Ordinal);

            using (new FileStream(bad, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var blocked = TomlDocument.Load(bad);
                Assert.Equal(TomlRead.Unreadable, blocked.Kind);
                Assert.True(blocked.Message.Length > 0);
            }

            var withMark = new byte[] { 0xEF, 0xBB, 0xBF };
            File.WriteAllBytes(marked, [.. withMark, .. "name = \"mine\"\n"u8]);
            var parsed = TomlDocument.Load(marked);
            Assert.Equal(TomlRead.Parsed, parsed.Kind);
            Assert.True(parsed.Bom);
            Assert.Equal("mine", parsed.Document!.Values["name"]);

            File.WriteAllBytes(plain, "name = \"mine\"\n"u8.ToArray());
            var withoutMark = TomlDocument.Load(plain);
            Assert.Equal(TomlRead.Parsed, withoutMark.Kind);
            Assert.False(withoutMark.Bom);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
