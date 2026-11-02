namespace Winshell.Tests;

public class WingetTokenTests
{
    [Fact]
    public void Wingetcreate_reads_the_token_from_the_environment()
    {
        var yaml = File.ReadAllText(Path.Combine(Repo.Root, ".github", "workflows", "package.yml"));
        Assert.Contains("WINGET_CREATE_GITHUB_TOKEN: ${{ secrets.WINGET_TOKEN }}", yaml);
        Assert.DoesNotContain("--token", yaml);
    }
}
