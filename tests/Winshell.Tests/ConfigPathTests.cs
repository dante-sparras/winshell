namespace Winshell.Tests;

/// <summary>Config roots. Every path is under a temp folder this class creates.</summary>
public sealed class ConfigPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-config-tests", Guid.NewGuid().ToString("N"));

    public ConfigPathTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void The_default_root_is_roaming_app_data()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "winshell"),
            ConfigPaths.RootFrom(null));
        Assert.Equal(ConfigPaths.RootFrom("  "), ConfigPaths.RootFrom(null));
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
