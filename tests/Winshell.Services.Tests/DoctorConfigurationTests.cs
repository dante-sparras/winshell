using Winshell.Diagnostics;

namespace Winshell.Services.Tests;

/// <summary>
/// The configuration-project checks read the project file as data. They never build. Every file is under a temp
/// folder this class creates.
/// </summary>
public sealed class DoctorConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public DoctorConfigurationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Plain = "<Project Sdk=\"Microsoft.NET.Sdk\" />";

    private static readonly DateTime Long = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void No_project_is_the_default_shell_and_nothing_to_build()
    {
        var checks = DoctorProject.Configuration(Path.Combine(_root, "missing"));

        Assert.Equal(["project", "downloaded", "built", "build"], checks.Select(c => c.Id));
        Assert.Equal(CheckStatus.Pass, checks[0].Status);
        Assert.Equal("No configuration project: Winshell runs its default shell.", checks[0].Message);
        Assert.All(checks.Skip(1), c => Assert.Equal(CheckStatus.Pass, c.Status));
        Assert.Contains("No configuration project", checks[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_projects_say_to_pick_one_and_are_not_built()
    {
        Project("a\\A.csproj");
        Project("b\\B.csproj");

        var project = Assert.Single(DoctorProject.Configuration(_root), c => c.Id == "project");

        Assert.Equal(CheckStatus.Fail, project.Status);
        Assert.Contains("pick one with -c: a, b", project.Message, StringComparison.Ordinal);
        Assert.Contains("winshell -c", project.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_names_itself_and_a_missing_program_fails_once_and_asks_for_one_run()
    {
        var file = Project("Mine.csproj");
        var before = Entries();

        var checks = DoctorProject.Configuration(_root);

        Assert.Equal(CheckStatus.Pass, checks[0].Status);
        Assert.Equal(file, checks[0].Message);
        Assert.Equal(CheckStatus.Pass, checks[1].Status);
        Assert.Equal("Nothing the build would run was downloaded.", checks[1].Message);
        Assert.Equal(CheckStatus.Fail, checks[2].Status);
        Assert.Equal("Mine has not been built.", checks[2].Message);
        Assert.Equal("Run winshell run once to build it.", checks[2].Fix);
        Assert.Equal(CheckStatus.Pass, checks[3].Status);
        Assert.Equal("Nothing to compare until Mine is built.", checks[3].Message);
        Assert.Null(checks[3].Fix);
        Assert.Equal(before, Entries());
    }

    [Fact]
    public void A_built_program_passes_until_a_source_is_newer()
    {
        var file = Project("Mine.csproj", "<Project><PropertyGroup><AssemblyName>Shell</AssemblyName></PropertyGroup></Project>");
        File.SetLastWriteTimeUtc(file, Long);
        var program = Path.Combine(_root, "bin", "shell", "Shell.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(program)!);
        File.WriteAllText(program, "");
        File.SetLastWriteTimeUtc(program, Long.AddMinutes(1));

        var built = DoctorProject.Configuration(_root);
        Assert.Equal(CheckStatus.Pass, built[2].Status);
        Assert.Equal(program, built[2].Message);
        Assert.Equal(CheckStatus.Pass, built[3].Status);
        Assert.Equal("Mine is up to date.", built[3].Message);

        var source = Project("Views\\Mine.xaml");
        File.SetLastWriteTimeUtc(source, Long.AddMinutes(2));
        var stale = DoctorProject.Configuration(_root);
        Assert.Equal(CheckStatus.Pass, stale[2].Status);
        Assert.Equal(CheckStatus.Warn, stale[3].Status);
        Assert.Equal("A file in Mine is newer than the program.", stale[3].Message);
    }

    [Theory]
    [InlineData("Mine.csproj")]
    [InlineData("Directory.Build.props")]
    public void A_downloaded_file_the_build_would_run_fails_and_is_not_built(string name)
    {
        Project("Mine.csproj");
        var marked = Path.Combine(_root, name);
        File.AppendAllText(marked, "");
        File.WriteAllText(marked + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        var before = Entries();

        var downloaded = Assert.Single(DoctorProject.Configuration(_root), c => c.Id == "downloaded");

        Assert.Equal(CheckStatus.Fail, downloaded.Status);
        Assert.StartsWith(marked + " was downloaded from the internet", downloaded.Message, StringComparison.Ordinal);
        Assert.Contains("Unblock-File", downloaded.Fix, StringComparison.Ordinal);
        Assert.Equal(before, Entries());
    }

    private string Project(string relative, string text = Plain)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string[] Entries() => Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
}
