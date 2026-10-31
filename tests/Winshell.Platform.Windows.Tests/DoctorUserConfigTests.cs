using Winshell.Platform.Windows.Diagnostics;
using static Winshell.Platform.Windows.Diagnostics.Doctor;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// The config check reads the TOML files a shell loads, and the Windows check mentions transparency and a contrast
/// theme. Neither adds a check. Every file is under a temp folder this class creates.
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
    public void A_broken_user_file_warns_and_a_qml_failure_stays_a_failure()
    {
        var warned = Config(@"C:\c\shell.qml", null, new ConfigScan(1, []), null, userFiles: [@"C:\c\preferences.toml[1:14]: unexpected token"]);

        Assert.Equal(CheckStatus.Warn, warned.Status);
        Assert.Contains(@"C:\c\shell.qml: 1 QML file read without errors. C:\c\preferences.toml", warned.Message, StringComparison.Ordinal);
        Assert.Contains("until the file parses", warned.Fix, StringComparison.Ordinal);

        var failed = Config(@"C:\c\shell.qml", null, new ConfigScan(1, [@"C:\c\shell.qml[1:1]: x"]), null, userFiles: [@"C:\c\launcher.toml[2:1]: bad"]);

        Assert.Equal(CheckStatus.Fail, failed.Status);
        Assert.Equal(@"C:\c\shell.qml[1:1]: x. C:\c\launcher.toml[2:1]: bad", failed.Message);
        Assert.Contains("Correct that line", failed.Fix, StringComparison.Ordinal);

        var downloaded = Config(@"C:\c\shell.qml", null, new ConfigScan(0, []), null, downloaded: true, userFiles: [@"C:\c\preferences.toml[1:1]: bad"]);
        Assert.Equal(@"C:\c\shell.qml was downloaded from the internet, so Winshell does not run it. It would run as you.", downloaded.Message);
    }

    [Fact]
    public void A_missing_or_readable_user_file_leaves_the_config_check_alone()
    {
        var config = Path.Combine(_dir, "config");
        var shell = Path.Combine(config, "work");
        Directory.CreateDirectory(shell);
        var root = Path.Combine(shell, "shell.qml");
        File.WriteAllText(root, "import Winshell\nShellRoot {}\n");
        File.WriteAllText(Path.Combine(shell, "preferences.toml"), "# keep\ncolorScheme = \"dark\"\n");
        File.WriteAllText(Path.Combine(shell, "launcher.toml"), "");

        Assert.Empty(DoctorProbe.UserConfigProblems(root, config, Path.Combine(_dir, "local")));
    }

    [Fact]
    public void Preferences_launcher_and_quick_settings_are_read_where_the_shell_loads_them()
    {
        var config = Path.Combine(_dir, "config");
        var shell = Path.Combine(config, "Work");
        Directory.CreateDirectory(shell);
        var root = Path.Combine(shell, "shell.qml");
        File.WriteAllText(root, "import Winshell\nShellRoot {}\n");
        var preferences = Path.Combine(shell, "preferences.toml");
        var launcher = Path.Combine(shell, "launcher.toml");
        var tiles = Path.Combine(_dir, "local", "state", "by-shell", "work", "quick-settings.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(tiles)!);
        File.WriteAllText(preferences, "colorScheme =\n");
        File.WriteAllText(launcher, "pins = [\n");
        File.WriteAllText(tiles, "pinned = [\n");
        // A file in the config folder itself is not this shell's preferences.
        File.WriteAllText(Path.Combine(config, "preferences.toml"), "colorScheme =\n");

        var problems = DoctorProbe.UserConfigProblems(root, config, Path.Combine(_dir, "local"));

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.StartsWith(preferences + "[", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith(launcher + "[", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith(tiles + "[", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains(Path.Combine(config, "preferences.toml"), StringComparison.Ordinal));
    }

    [Fact]
    public void A_state_directory_pragma_is_where_quick_settings_are_read()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        var root = Path.Combine(config, "shell.qml");
        File.WriteAllText(root, "//@ pragma StateDir $BASE/tiles\nimport Winshell\nShellRoot {}\n");
        var tiles = Path.Combine(_dir, "local", "state", "tiles", "quick-settings.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(tiles)!);
        File.WriteAllText(tiles, "pinned = [\n");

        var problem = Assert.Single(DoctorProbe.UserConfigProblems(root, config, Path.Combine(_dir, "local")));

        Assert.StartsWith(tiles + "[", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_shell_reads_preferences_from_the_config_folder()
    {
        var config = Path.Combine(_dir, "config");
        var local = Path.Combine(_dir, "local");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(Path.Combine(local, "default-shell"));
        var preferences = Path.Combine(config, "preferences.toml");
        var ignored = Path.Combine(local, "default-shell", "preferences.toml");
        var launcher = Path.Combine(local, "default-shell", "launcher.toml");
        File.WriteAllText(preferences, "colorScheme =\n");
        File.WriteAllText(ignored, "colorScheme =\n");
        File.WriteAllText(launcher, "pins = [\"explorer\"]\n");

        var problem = Assert.Single(DoctorProbe.UserConfigProblems(null, config, local));

        Assert.StartsWith(preferences + "[", problem, StringComparison.Ordinal);
        var checks = Run(new DoctorContext(Environment.ProcessPath!, config, null, null, [], local, Path.Combine(_dir, "crashes")));
        var check = checks.Single(c => c.Id == "config");
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("default shell", check.Message, StringComparison.Ordinal);
        Assert.Contains("preferences.toml", check.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(check.Fix));
    }

    [Fact]
    public void A_shell_outside_the_config_folder_keeps_preferences_in_that_folder()
    {
        var config = Path.Combine(_dir, "config");
        var elsewhere = Path.Combine(_dir, "elsewhere");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(elsewhere);
        var root = Path.Combine(elsewhere, "shell.qml");
        File.WriteAllText(root, "import Winshell\nShellRoot {}\n");
        File.WriteAllText(Path.Combine(elsewhere, "preferences.toml"), "colorScheme = \"dark\"\n");
        var preferences = Path.Combine(config, "preferences.toml");
        File.WriteAllText(preferences, "colorScheme =\n");

        var problem = Assert.Single(DoctorProbe.UserConfigProblems(root, config, Path.Combine(_dir, "local")));

        Assert.StartsWith(preferences + "[", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_file_that_cannot_be_read_warns_and_names_it()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        var root = Path.Combine(config, "shell.qml");
        File.WriteAllText(root, "import Winshell\nShellRoot {}\n");
        var preferences = Path.Combine(config, "preferences.toml");
        using var locked = new FileStream(preferences, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        var problem = Assert.Single(DoctorProbe.UserConfigProblems(root, config, Path.Combine(_dir, "local")));

        Assert.StartsWith(preferences + ": could not be read:", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_downloaded_config_does_not_read_its_user_files()
    {
        var config = Path.Combine(_dir, "downloaded");
        Directory.CreateDirectory(config);
        var root = Path.Combine(config, "shell.qml");
        File.WriteAllText(root, "import Winshell\nShellRoot {\n");
        File.WriteAllText(Path.Combine(config, "preferences.toml"), "colorScheme =\n");
        File.WriteAllText(root + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        var checks = Run(new DoctorContext(Environment.ProcessPath!, config, root, null, [], Path.Combine(_dir, "local"), Path.Combine(_dir, "crashes")));

        var check = checks.Single(c => c.Id == "config");
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.EndsWith("shell.qml was downloaded from the internet, so Winshell does not run it. It would run as you.", check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("preferences.toml", check.Message, StringComparison.Ordinal);
    }
}
