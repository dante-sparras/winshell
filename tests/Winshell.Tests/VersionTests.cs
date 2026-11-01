using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winshell.Tests;

public class VersionTests
{
    [Fact]
    public void The_cli_prints_the_release_please_version()
    {
        var manifest = ManifestVersion();
        var (code, output, error) = WinshellCli.Run(["--version"]);
        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Matches($"^winshell {Regex.Escape(manifest)}(\\+\\S+)?\\r?\\n$", output);
    }

    private static string ManifestVersion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, ".release-please-manifest.json");
            if (File.Exists(path))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                return json.RootElement.GetProperty(".").GetString() ?? throw new InvalidOperationException("The release-please manifest has no version.");
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("The release-please manifest is not above the test output.");
    }
}
