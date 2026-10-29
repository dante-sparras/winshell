namespace Winshell.Tests;

public class PackagePathsTests
{
    [Fact]
    public void A_docs_only_push_does_not_build_packages()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "package.yml"));
        Assert.Contains("paths-ignore:", yaml);
        Assert.Contains("\"docs/**\"", yaml);
        Assert.Contains("\"**/*.md\"", yaml);
        Assert.Contains("retention-days: 14", yaml);
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
