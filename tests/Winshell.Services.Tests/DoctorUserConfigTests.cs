using Winshell.Diagnostics;
using static Winshell.Diagnostics.Doctor;

namespace Winshell.Services.Tests;

/// <summary>
/// The project check reads the <c>preferences.toml</c> the configuration guide keeps in the config folder, and the
/// Windows check mentions transparency and a contrast theme. Neither adds a check. Every file is under a temp folder this class creates.
/// </summary>
public sealed class DoctorUserConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-doctor-user-config", Guid.NewGuid().ToString("N"));

    public DoctorUserConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData(26300, 1, "26H2", false, CheckStatus.Pass)]
    [InlineData(22000, 1, "21H2", false, CheckStatus.Warn)]
    [InlineData(19045, 1, "22H2", false, CheckStatus.Fail)]
    public void Windows_mentions_transparency_and_a_contrast_theme_without_changing_the_result(int build, int revision, string? version, bool server, CheckStatus status)
    {
        var plain = Doctor.Windows(build, revision, version, server);
        var both = Doctor.Windows(build, revision, version, server, transparencyOff: true, contrastTheme: true);
        var transparency = Doctor.Windows(build, revision, version, server, transparencyOff: true);
        var contrast = Doctor.Windows(build, revision, version, server, contrastTheme: true);

        Assert.Equal(status, plain.Status);
        Assert.Equal(plain.Status, both.Status);
        Assert.Equal(plain.Fix, both.Fix);
        Assert.Equal(plain.Message, Doctor.Windows(build, revision, version, server, transparencyOff: false, contrastTheme: false).Message);
        Assert.StartsWith(plain.Message + ". ", both.Message, StringComparison.Ordinal);
        Assert.Contains("Transparency effects are off, so Mica and Acrylic look flat.", both.Message, StringComparison.Ordinal);
        Assert.Contains("A contrast theme is on.", both.Message, StringComparison.Ordinal);
        Assert.Contains("Transparency effects are off", transparency.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("contrast theme", transparency.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A contrast theme is on.", contrast.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Transparency effects", contrast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_preferences_file_warns_and_a_project_failure_stays_a_failure()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "Mine.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var preferences = Path.Combine(config, "preferences.toml");
        File.WriteAllText(preferences, "colorScheme =\n");
        var local = Path.Combine(_dir, "local");
        var crashes = Path.Combine(_dir, "crashes");

        var warned = Run(Context(config, local, crashes), DoctorFacts.Pc).Single(c => c.Id == "project");

        Assert.Equal(CheckStatus.Warn, warned.Status);
        Assert.Contains("Mine.csproj", warned.Message, StringComparison.Ordinal);
        Assert.Contains(Printed(preferences), warned.Message, StringComparison.Ordinal);
        Assert.Contains("until the file parses", warned.Fix, StringComparison.Ordinal);

        var failed = Run(Context(config, local, crashes, problem: "Winshell cannot read the config folder: denied"), DoctorFacts.Pc).Single(c => c.Id == "project");

        Assert.Equal(CheckStatus.Fail, failed.Status);
        Assert.Equal("Winshell cannot read the config folder: denied", failed.Message);
        Assert.DoesNotContain("preferences.toml", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_project_a_broken_preferences_file_is_not_mentioned()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "preferences.toml"), "colorScheme =\n");

        var check = Run(Context(config, Path.Combine(_dir, "local"), Path.Combine(_dir, "crashes")), DoctorFacts.Pc).Single(c => c.Id == "project");

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Equal("No configuration project, so winshell run starts nothing.", check.Message);
        Assert.Equal("Make one with dotnet new winshell, as the configuration guide shows.", check.Fix);
    }

    [Fact]
    public void A_missing_or_readable_preferences_file_is_no_problem()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);

        Assert.Null(DoctorProbe.PreferencesProblem(config));
        File.WriteAllText(Path.Combine(config, "preferences.toml"), "# keep\ncolorScheme = \"dark\"\n");
        Assert.Null(DoctorProbe.PreferencesProblem(config));
    }

    [Fact]
    public void Preferences_are_read_from_the_config_folder_and_not_the_project_folder()
    {
        var config = Path.Combine(_dir, "config");
        var shell = Path.Combine(config, "Work");
        Directory.CreateDirectory(shell);
        var projectFile = Path.Combine(shell, "Work.csproj");
        File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var preferences = Path.Combine(config, "preferences.toml");
        File.WriteAllText(preferences, "colorScheme =\n");
        var decoy = Path.Combine(shell, "preferences.toml");
        File.WriteAllText(decoy, "colorScheme =\n");

        var check = Run(Context(config, Path.Combine(_dir, "local"), Path.Combine(_dir, "crashes"), projectFile), DoctorFacts.Pc).Single(c => c.Id == "project");

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains(Printed(preferences) + "[", check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Printed(decoy), check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_preferences_file_that_cannot_be_read_is_named_with_why()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        var preferences = Path.Combine(config, "preferences.toml");
        using var locked = new FileStream(preferences, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        var problem = DoctorProbe.PreferencesProblem(config);

        Assert.StartsWith(preferences + ": could not be read:", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_downloaded_project_does_not_read_its_user_files()
    {
        var config = Path.Combine(_dir, "downloaded");
        Directory.CreateDirectory(config);
        var project = Path.Combine(config, "Mine.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(project + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        File.WriteAllText(Path.Combine(config, "preferences.toml"), "colorScheme =\n");

        var checks = Run(Context(config, Path.Combine(_dir, "local"), Path.Combine(_dir, "crashes"), project), DoctorFacts.Pc);

        var downloaded = checks.Single(c => c.Id == "downloaded");
        Assert.Equal(CheckStatus.Fail, downloaded.Status);
        Assert.Contains("was downloaded from the internet", downloaded.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("preferences.toml", checks.Single(c => c.Id == "project").Message, StringComparison.Ordinal);
    }

    private static DoctorContext Context(string config, string local, string crashes, string? project = null, string? problem = null) =>
        new(Environment.ProcessPath!, config, project, problem, [], local, crashes);

    /// <summary>The path <see cref="Doctor.Run"/> prints: the user's local folder is <c>%LOCALAPPDATA%</c>.</summary>
    private static string Printed(string path) => path.Replace(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "%LOCALAPPDATA%",
        StringComparison.OrdinalIgnoreCase);
}
