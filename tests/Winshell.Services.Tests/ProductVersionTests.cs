using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winshell.Services.Tests;

public class ProductVersionTests
{
    [Theory]
    [InlineData("0.3.0", 0, 3, 0)]
    [InlineData("0.3.0+abcdef", 0, 3, 0)]
    [InlineData("0.3.0-dev", 0, 3, 0)]
    [InlineData("0.0.0-dev+sha", 0, 0, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1", 1, 0, 0)]
    [InlineData("10.20.30-beta.1+build.7", 10, 20, 30)]
    [InlineData("", 0, 0, 0)]
    [InlineData("dev", 0, 0, 0)]
    [InlineData(null, 0, 0, 0)]
    public void Parse_reads_the_release_numbers(string? text, int major, int minor, int patch)
    {
        var version = ProductVersion.Parse(text);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Fact]
    public void The_assembly_version_is_the_release_please_manifest()
    {
        var manifest = ManifestVersion();
        var informational = typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Matches($"^{Regex.Escape(manifest)}(\\+\\S+)?$", informational);
        Assert.Equal(informational, ProductVersion.Current.Text);
        var parsed = ProductVersion.Parse(manifest);
        Assert.Equal(parsed.Major, ProductVersion.Current.Major);
        Assert.Equal(parsed.Minor, ProductVersion.Current.Minor);
        Assert.Equal(parsed.Patch, ProductVersion.Current.Patch);
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
