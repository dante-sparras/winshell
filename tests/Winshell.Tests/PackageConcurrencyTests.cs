namespace Winshell.Tests;

public class PackageConcurrencyTests
{
    [Fact]
    public void A_release_tag_serializes_the_whole_package_workflow()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "package.yml"));
        var at = yaml.IndexOf("concurrency:", StringComparison.Ordinal);
        var jobs = yaml.IndexOf("\njobs:", StringComparison.Ordinal);
        Assert.True(at >= 0 && jobs > at, "The concurrency group belongs on the workflow, before jobs.");
        Assert.Equal(1, yaml.Split("concurrency:").Length - 1);
        Assert.Contains("group: package-${{ inputs.tag || github.run_id }}", yaml);
        Assert.Contains("cancel-in-progress: false", yaml);
        Assert.DoesNotContain("package-release-", yaml);
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
