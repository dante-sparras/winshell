namespace Winshell.Tests;

public class WingetTokenTests
{
    [Fact]
    public void Wingetcreate_reads_the_token_from_the_environment()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "package.yml"));
        Assert.Contains("WINGET_CREATE_GITHUB_TOKEN: ${{ secrets.WINGET_TOKEN }}", yaml);
        Assert.DoesNotContain("--token", yaml);
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
