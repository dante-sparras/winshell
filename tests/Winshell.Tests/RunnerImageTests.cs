namespace Winshell.Tests;

public class RunnerImageTests
{
    [Fact]
    public void No_job_follows_a_moving_runner_image()
    {
        foreach (var workflow in Directory.EnumerateFiles(Path.Combine(Repo.Root, ".github", "workflows")))
            Assert.DoesNotContain("-latest", File.ReadAllText(workflow));
    }
}
