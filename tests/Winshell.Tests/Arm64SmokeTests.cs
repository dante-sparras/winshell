namespace Winshell.Tests;

public class Arm64SmokeTests
{
    [Fact]
    public void The_package_workflow_runs_the_arm64_executable()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "package.yml"));
        Assert.Contains("name: arm64", yaml);
        Assert.Contains("runs-on: windows-11-arm", yaml);
        Assert.Contains("winshell-*-win-arm64.zip", yaml);
        Assert.Contains("winshell.exe --version", yaml);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "winshell.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("The tests run outside the repository.");
    }
}
