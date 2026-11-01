using System.Diagnostics;

namespace Winshell.Tests;

/// <summary>Config roots and the one-time copy. Every path is under a temp folder this class creates.</summary>
public sealed class ConfigPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-config-tests", Guid.NewGuid().ToString("N"));

    public ConfigPathTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void The_default_root_is_roaming_app_data_and_the_old_root_is_dot_config()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "winshell"),
            ConfigPaths.RootFrom(null));
        Assert.Equal(ConfigPaths.RootFrom("  "), ConfigPaths.RootFrom(null));
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "winshell"),
            ConfigPaths.LegacyRoot());
        Assert.Equal(Path.Combine(ShellLocationsState(), "legacy-config-copied"), ConfigPaths.MarkerPath());
    }

    [Fact]
    public void An_override_is_the_config_root()
    {
        var dir = Path.Combine(_root, "custom");
        Assert.Equal(dir, ConfigPaths.RootFrom(dir));
        Assert.Equal(dir, ConfigPaths.RootFrom("  " + dir + "  "));
        Assert.Equal(Path.GetFullPath("winshell-relative"), ConfigPaths.RootFrom("winshell-relative"));
    }

    [Fact]
    public void Migration_copies_files_and_leaves_the_old_folder()
    {
        var legacy = Path.Combine(_root, "legacy");
        var dest = Path.Combine(_root, "dest");
        var marker = Path.Combine(_root, "marker");
        Directory.CreateDirectory(Path.Combine(legacy, "bar"));
        File.WriteAllText(Path.Combine(legacy, "shell.qml"), "old");
        File.WriteAllText(Path.Combine(legacy, "bar", "a.qml"), "a");

        var message = ConfigPaths.Migrate(legacy, dest, marker);

        Assert.NotNull(message);
        Assert.Contains("left in place", message, StringComparison.Ordinal);
        Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "shell.qml")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(dest, "bar", "a.qml")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(legacy, "shell.qml")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(legacy, "bar", "a.qml")));
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public void Migration_does_not_overwrite_and_runs_once()
    {
        var legacy = Path.Combine(_root, "legacy");
        var dest = Path.Combine(_root, "dest");
        var marker = Path.Combine(_root, "marker");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(legacy, "shell.qml"), "from-old");
        File.WriteAllText(Path.Combine(legacy, "kept.qml"), "from-old");
        File.WriteAllText(Path.Combine(dest, "shell.qml"), "already-here");

        var message = ConfigPaths.Migrate(legacy, dest, marker);

        Assert.NotNull(message);
        Assert.Equal("already-here", File.ReadAllText(Path.Combine(dest, "shell.qml")));
        Assert.Equal("from-old", File.ReadAllText(Path.Combine(dest, "kept.qml")));
        Assert.Equal("from-old", File.ReadAllText(Path.Combine(legacy, "shell.qml")));

        File.WriteAllText(Path.Combine(legacy, "later.qml"), "later");
        Assert.Null(ConfigPaths.Migrate(legacy, dest, marker));
        Assert.False(File.Exists(Path.Combine(dest, "later.qml")));
        Assert.Equal("later", File.ReadAllText(Path.Combine(legacy, "later.qml")));
    }

    [Fact]
    public void Migration_records_a_missing_old_folder_without_creating_the_new_one()
    {
        var dest = Path.Combine(_root, "dest");
        var marker = Path.Combine(_root, "state", "marker");

        Assert.Null(ConfigPaths.Migrate(Path.Combine(_root, "missing"), dest, marker));

        Assert.True(File.Exists(marker));
        Assert.False(Directory.Exists(dest));
    }

    [Fact]
    public void Migration_refuses_to_copy_a_folder_into_itself()
    {
        var legacy = Path.Combine(_root, "legacy");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "shell.qml"), "old");
        var nested = Path.Combine(legacy, "nested");

        Assert.Null(ConfigPaths.Migrate(legacy, nested, Path.Combine(_root, "marker")));

        Assert.False(File.Exists(Path.Combine(nested, "shell.qml")));
        Assert.True(File.Exists(Path.Combine(legacy, "shell.qml")));
    }

    [Fact]
    public void The_cli_uses_winshell_config_dir_and_ignores_xdg_config_home()
    {
        var config = Path.Combine(_root, "configs");
        var xdg = Path.Combine(_root, "xdg-parent");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(xdg);

        var help = Run(config, xdg, "--help");
        Assert.Equal(0, help.Code);
        Assert.Contains(config, help.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(xdg, help.Output, StringComparison.OrdinalIgnoreCase);

        var missing = Run(config, xdg, "-c", "missing");
        Assert.Equal(1, missing.Code);
        Assert.Contains(config, missing.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing", missing.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(xdg, missing.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".config", missing.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static string ShellLocationsState() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winshell", "state");

    private static (int Code, string Output, string Error) Run(string configDir, string xdgHome, params string[] args) =>
        WinshellCli.Run(args, (ConfigPaths.OverrideVariable, configDir), ("XDG_CONFIG_HOME", xdgHome));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }
}
