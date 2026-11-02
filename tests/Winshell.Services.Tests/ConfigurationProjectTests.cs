namespace Winshell.Services.Tests;

public sealed class ConfigurationProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _commands = [];

    public ConfigurationProjectTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Plain = "<Project Sdk=\"Microsoft.NET.Sdk\" />";

    private string Project(string relative, string text = Plain)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private ConfigurationProject Found(string? path = null, string? name = null)
    {
        var project = ConfigurationProject.Find(path, name, _root, out var error);
        Assert.Null(error);
        return Assert.IsType<ConfigurationProject>(project);
    }

    private string? Refusal(string? path = null, string? name = null)
    {
        Assert.Null(ConfigurationProject.Find(path, name, _root, out var error));
        return error;
    }

    private CommandResult Builds(string file, IReadOnlyList<string> arguments, string folder)
    {
        _commands.Add(file + " " + string.Join(' ', arguments));
        return new CommandResult(0, "");
    }

    private static void Built(ConfigurationProject project, DateTime when)
    {
        Directory.CreateDirectory(project.OutputFolder);
        File.WriteAllText(project.Program, "");
        File.SetLastWriteTimeUtc(project.Program, when);
    }

    private static readonly DateTime Long = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_path_is_a_project_or_a_folder_with_one()
    {
        var file = Project("mine\\Bar.csproj");

        Assert.Equal(file, Found(path: file).ProjectFile);
        Assert.Equal(file, Found(path: Path.Combine(_root, "mine")).ProjectFile);
        Assert.StartsWith("No .csproj at ", Refusal(path: Path.Combine(_root, "other")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_with_two_projects_says_to_name_one()
    {
        Project("mine\\A.csproj");
        Project("mine\\B.csproj");

        Assert.Contains("A.csproj, B.csproj", Refusal(path: Path.Combine(_root, "mine")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_is_a_folder_of_the_root()
    {
        var file = Project("work\\Work.csproj");

        Assert.Equal(file, Found(name: "work").ProjectFile);
        Assert.Equal($"No config named \"home\" in {_root}", Refusal(name: "home"));
    }

    [Fact]
    public void The_root_comes_before_default_and_default_before_a_lone_folder()
    {
        var lone = Project("lone\\Lone.csproj");
        Assert.Equal(lone, Found().ProjectFile);

        var fallback = Project("default\\Default.csproj");
        Assert.Equal(fallback, Found().ProjectFile);

        var top = Project("Top.csproj");
        Assert.Equal(top, Found().ProjectFile);
    }

    [Fact]
    public void Two_folders_with_projects_say_to_pick_one()
    {
        Project("a\\A.csproj");
        Project("b\\B.csproj");

        Assert.Contains("pick one with -c: a, b", Refusal(), StringComparison.Ordinal);
    }

    [Fact]
    public void No_project_at_all_is_no_error_so_the_default_shell_runs()
    {
        Assert.Null(ConfigurationProject.Find(null, null, Path.Combine(_root, "missing"), out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("<Project><PropertyGroup><AssemblyName>Mine</AssemblyName></PropertyGroup></Project>", "Mine.exe")]
    [InlineData("<Project><PropertyGroup><AssemblyName>$(Other)</AssemblyName></PropertyGroup></Project>", "Bar.exe")]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\" />", "Bar.exe")]
    [InlineData("not xml", "Bar.exe")]
    public void The_program_is_named_by_the_project(string text, string expected)
    {
        var project = Found(path: Project("Bar.csproj", text));

        Assert.Equal(Path.Combine(_root, "bin", "shell", expected), project.Program);
    }

    [Fact]
    public void A_build_is_needed_with_no_program_and_after_a_newer_source()
    {
        var file = Project("Bar.csproj");
        var source = Project("Views\\Bar.xaml");
        File.SetLastWriteTimeUtc(file, Long);
        File.SetLastWriteTimeUtc(source, Long);
        var project = Found(path: file);
        Assert.True(project.NeedsBuild());

        Built(project, Long.AddMinutes(1));
        Assert.False(project.NeedsBuild());

        File.SetLastWriteTimeUtc(source, Long.AddMinutes(2));
        Assert.True(project.NeedsBuild());
    }

    [Fact]
    public void What_a_build_or_an_editor_writes_needs_no_build()
    {
        var file = Project("Bar.csproj");
        File.SetLastWriteTimeUtc(file, Long);
        var project = Found(path: file);
        Built(project, Long.AddMinutes(1));

        File.SetLastWriteTimeUtc(Project("obj\\project.assets.json"), Long.AddMinutes(2));
        File.SetLastWriteTimeUtc(Project(".vs\\state.bin"), Long.AddMinutes(2));

        Assert.False(project.NeedsBuild());
    }

    [Fact]
    public void Prepare_builds_when_a_source_is_newer_and_only_then()
    {
        var file = Project("Bar.csproj");
        File.SetLastWriteTimeUtc(file, Long);
        var project = Found(path: file);

        Assert.Null(project.Prepare(noBuild: false, Builds));
        var command = Assert.Single(_commands);
        Assert.StartsWith($"dotnet build {file} -c Release -o {project.OutputFolder}", command, StringComparison.Ordinal);

        Built(project, Long.AddMinutes(1));
        Assert.Null(project.Prepare(noBuild: false, Builds));
        Assert.Single(_commands);
    }

    [Fact]
    public void No_build_runs_what_the_last_build_left_even_with_a_newer_source()
    {
        var file = Project("Bar.csproj");
        var project = Found(path: file);
        Built(project, Long);

        Assert.Null(project.Prepare(noBuild: true, Builds));

        Assert.Empty(_commands);
    }

    [Fact]
    public void No_build_with_no_program_says_to_build_it()
    {
        var project = Found(path: Project("Bar.csproj"));

        Assert.Equal("Bar has not been built. Run winshell run once to build it.", project.Prepare(noBuild: true, Builds));
        Assert.Empty(_commands);
    }

    [Theory]
    [InlineData("Bar.csproj")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    public void A_downloaded_project_never_reaches_a_build(string name)
    {
        var project = Found(path: Project("Bar.csproj"));
        var marked = Path.Combine(_root, name);
        File.AppendAllText(marked, "");
        File.WriteAllText(marked + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        var refusal = project.Prepare(noBuild: false, Builds);

        Assert.StartsWith(name + " was downloaded", refusal, StringComparison.Ordinal);
        Assert.Empty(_commands);
    }

    [Fact]
    public void A_failed_build_gives_the_compiler_errors_once_each_without_the_project()
    {
        var project = Found(path: Project("Bar.csproj"));
        const string output = """
            Bar.cs(1,1): error CS1002: ; expected [C:\x\Bar.csproj]
            Bar.cs(1,1): error CS1002: ; expected [C:\x\Bar.csproj]
              noise
            Build FAILED.
            """;

        var why = project.Prepare(noBuild: false, (_, _, _) => new CommandResult(1, output));

        Assert.Equal("Bar did not build.\nBar.cs(1,1): error CS1002: ; expected", why);
    }

    [Fact]
    public void A_failure_with_no_compiler_error_keeps_its_last_lines()
    {
        var project = Found(path: Project("Bar.csproj"));

        var why = project.Prepare(noBuild: false, (_, _, _) => new CommandResult(1, "first\nsecond"));

        Assert.Equal("Bar did not build.\nfirst\nsecond", why);
    }

    [Fact]
    public void A_missing_sdk_says_where_to_get_one()
    {
        var result = ConfigurationProject.Run(Path.Combine(_root, "no-such-dotnet.exe"), [], _root);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("dot.net/download", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_build_puts_the_program_where_the_project_says()
    {
        const string text = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>Mine</AssemblyName>
              </PropertyGroup>
            </Project>
            """;
        var project = Found(path: Project("Bar.csproj", text));
        Project("Program.cs", "System.Console.Write(\"built\");");

        var why = project.Prepare(noBuild: false, ConfigurationProject.Run);

        Assert.Null(why);
        Assert.False(project.NeedsBuild());
        Assert.Equal((0, "built"), ToTuple(ConfigurationProject.Run(project.Program, [], project.Folder)));
    }

    private static (int, string) ToTuple(CommandResult result) => (result.ExitCode, result.Output);
}
