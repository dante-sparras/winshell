namespace Winshell.Tests;

public class ModuleHashTests
{
    [Fact]
    public void The_installer_job_checks_the_gallery_nupkg_hashes()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        Assert.Contains("PESTER_VERSION: 5.6.1", yaml);
        Assert.Contains("PESTER_SHA256: 37b3749f69accead67c366d81508977f62afc7cbb9c4db88c3a255a1f0b20da5", yaml);
        Assert.Contains("PSSCRIPTANALYZER_VERSION: 1.22.0", yaml);
        Assert.Contains("PSSCRIPTANALYZER_SHA256: 71bfb9eb58e19d4b662f4494a7d572a724b60e0588848dcff34195a0e08ae1be", yaml);
        Assert.Contains("Get-FileHash -Algorithm SHA256", yaml);
        Assert.DoesNotContain("Save-Module", yaml);
        Assert.DoesNotContain("Set-PSRepository", yaml);
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
