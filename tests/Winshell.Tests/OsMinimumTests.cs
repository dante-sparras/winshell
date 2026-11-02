namespace Winshell.Tests;

/// <summary>
/// The shipping minimum is Windows 11 22H2, build 22621, matching the target framework.
/// Doctor still warns below that build; its existing tests record that result.
/// </summary>
public class OsMinimumTests
{
    [Fact]
    public void Projects_and_packages_require_build_22621()
    {
        var root = Repo.Root;
        AssertPlatform(Path.Combine(root, "src", "Winshell", "Winshell.csproj"));
        AssertPlatform(Path.Combine(root, "src", "Winshell.Shell", "Winshell.Shell.csproj"));
        AssertPlatform(Path.Combine(root, "src", "Winshell.DefaultShell", "Winshell.DefaultShell.csproj"));
        AssertPlatform(Path.Combine(root, "src", "Winshell.Services", "Winshell.Services.csproj"));
        AssertPlatform(Path.Combine(root, "tests", "Winshell.Tests", "Winshell.Tests.csproj"));

        var package = File.ReadAllText(Path.Combine(root, "scripts", "package.ps1"));
        Assert.Contains("MinimumOSVersion: 10.0.22621.0", package, StringComparison.Ordinal);

        var manifest = File.ReadAllText(Path.Combine(root, "packaging", "msix", "AppxManifest.xml"));
        Assert.Contains("MinVersion=\"10.0.22621.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("MaxVersionTested=\"10.0.26200.0\"", manifest, StringComparison.Ordinal);
    }

    private static void AssertPlatform(string path)
    {
        var text = File.ReadAllText(path);
        Assert.Contains("<SupportedOSPlatformVersion>10.0.22621.0</SupportedOSPlatformVersion>", text, StringComparison.Ordinal);
    }
}
