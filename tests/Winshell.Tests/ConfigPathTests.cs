namespace Winshell.Tests;

/// <summary>Config roots. Every path is under a temp folder this class creates.</summary>
[Trait("Category", "Slow")]
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
    public void The_cli_uses_winshell_config_dir()
    {
        var config = Path.Combine(_root, "configs");
        Directory.CreateDirectory(config);

        var help = Run(config, "--help");
        Assert.Equal(0, help.Code);
        Assert.Contains(config, help.Output, StringComparison.OrdinalIgnoreCase);

        var missing = Run(config, "-c", "missing");
        Assert.Equal(1, missing.Code);
        Assert.Contains(config, missing.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing", missing.Error, StringComparison.Ordinal);
    }

    private static (int Code, string Output, string Error) Run(string configDir, params string[] args) =>
        WinshellCli.Run(args, (ConfigPaths.OverrideVariable, configDir));

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
