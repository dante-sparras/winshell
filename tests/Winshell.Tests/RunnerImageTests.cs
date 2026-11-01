namespace Winshell.Tests;

public class RunnerImageTests
{
    [Fact]
    public void Windows_jobs_pin_the_2025_runner()
    {
        var root = RepoRoot();
        foreach (var relative in new[] { Path.Combine(".github", "workflows", "ci.yml"), Path.Combine(".github", "workflows", "package.yml") })
        {
            var yaml = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("runs-on: windows-2025", yaml);
            Assert.DoesNotContain("windows-latest", yaml);
        }
    }

    [Fact]
    public void No_job_follows_a_moving_runner_image()
    {
        foreach (var workflow in Directory.EnumerateFiles(Path.Combine(RepoRoot(), ".github", "workflows")))
            Assert.DoesNotContain("-latest", File.ReadAllText(workflow));
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
