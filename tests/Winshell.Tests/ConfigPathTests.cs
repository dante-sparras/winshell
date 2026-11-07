namespace Winshell.Tests;

/// <summary>The CLI reads configurations from <c>WINSHELL_CONFIG_DIR</c>. Every path is under a temp folder this class creates.</summary>
[Trait("Category", "Slow")]
public sealed class ConfigPathTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "winshell-config-tests", Guid.NewGuid().ToString("N"));

    public ConfigPathTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void The_cli_uses_winshell_config_dir()
    {
        var config = Path.Combine(_root, "configs");
        Directory.CreateDirectory(config);

        var help = Run(config, "--help");
        help.AssertExitCode(0);
        Assert.Contains(config, help.Output, StringComparison.OrdinalIgnoreCase);

        var missing = Run(config, "-c", "missing");
        missing.AssertExitCode(1);
        Assert.Contains(config, missing.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing", missing.Error, StringComparison.Ordinal);
    }

    private static ChildProcess Run(string configDir, params string[] args)
    {
        return WinshellCli.Run(args, (ShellLocations.ConfigVariable, configDir));
    }

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
