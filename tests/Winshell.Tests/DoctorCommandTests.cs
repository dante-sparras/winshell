using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary><c>winshell doctor</c> as a process. Every config is under a temp folder this class creates. It never builds.</summary>
[Trait("Category", "Slow")]
public sealed class DoctorCommandTests : IDisposable
{
    private static readonly string[] Ids =
    [
        "windows", "dotnet", "project", "downloaded", "built", "build",
        "folders", "appBars", "dpi", "signIn", "notifications", "instances", "crashes"
    ];

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "winshell-doctor-tests", Guid.NewGuid().ToString("N"));

    public DoctorCommandTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Json_lists_every_check_and_an_unbuilt_project_fails()
    {
        var project = Path.Combine(_root, "broken");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Broken.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var result = Run("doctor", "--json", "-p", project);

        result.AssertExitCode(1);
        using var json = JsonDocument.Parse(result.Output);
        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal(Ids, checks.Select(c => c.GetProperty("id").GetString()));
        var built = checks.Single(c => c.GetProperty("id").GetString() == "built");
        Assert.Equal("fail", built.GetProperty("status").GetString());
        Assert.Contains("has not been built", built.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Run winshell run once", built.GetProperty("fix").GetString(), StringComparison.Ordinal);
        Assert.True(json.RootElement.GetProperty("summary").GetProperty("fail").GetInt32() >= 1);
    }

    [Fact]
    public void A_path_that_is_not_there_checks_no_project_beside_it()
    {
        var folder = Path.Combine(_root, "beside");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Other.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var missing = Path.Combine(folder, "missing");

        var result = Run("doctor", "--json", "-p", missing);

        result.AssertExitCode(1);
        using var json = JsonDocument.Parse(result.Output);
        var messages = json.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetString() ?? "", c => c.GetProperty("message").GetString());
        Assert.StartsWith("No .csproj at ", messages["project"], StringComparison.Ordinal);
        Assert.EndsWith(@"\beside\missing.", messages["project"], StringComparison.Ordinal);
        Assert.Equal("No configuration project to build.", messages["built"]);
    }

    [Fact]
    public void A_folder_given_with_p_is_not_searched_like_the_config_folder()
    {
        var folder = Path.Combine(_root, "holder");
        Directory.CreateDirectory(Path.Combine(folder, "default"));
        File.WriteAllText(Path.Combine(folder, "default", "Inner.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var result = Run("doctor", "--json", "-p", folder);

        result.AssertExitCode(1);
        using var json = JsonDocument.Parse(result.Output);
        var messages = json.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetString() ?? "", c => c.GetProperty("message").GetString());
        Assert.StartsWith("No .csproj at ", messages["project"], StringComparison.Ordinal);
        Assert.EndsWith(@"\holder.", messages["project"], StringComparison.Ordinal);
        Assert.Equal("No configuration project to build.", messages["built"]);
    }

    [Fact]
    public void Text_has_a_line_a_check_and_no_color_when_redirected()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        var (_, output, _) = RunIn(empty, "doctor");

        Assert.DoesNotContain("\e[", output);
        Assert.Contains("  WARN  Project", output, StringComparison.Ordinal);
        Assert.Matches(@"\d+ passed, \d+ warnings?, \d+ failed\.", output);
    }

    [Fact]
    public void Without_a_config_it_warns_that_nothing_runs_and_writes_nothing()
    {
        var empty = Path.Combine(_root, "none");
        Directory.CreateDirectory(empty);

        var (_, output, _) = RunIn(empty, "doctor", "--json");

        using var json = JsonDocument.Parse(output);
        var project = json.RootElement.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == "project");
        Assert.Equal("warn", project.GetProperty("status").GetString());
        Assert.Contains("starts nothing", project.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(empty));
    }

    [Fact]
    public void A_config_folder_it_cannot_list_fails_the_project_check_and_the_others_still_run()
    {
        var unreadable = Path.Combine(_root, "unreadable");
        Directory.CreateDirectory(unreadable);
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory,
            AccessControlType.Deny);
        var folder = new DirectoryInfo(unreadable);
        var acl = folder.GetAccessControl();
        acl.AddAccessRule(deny);
        folder.SetAccessControl(acl);
        ChildProcess result;
        try
        {
            result = RunIn(unreadable, "doctor", "--json");
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            folder.SetAccessControl(acl);
        }

        result.AssertExitCode(1);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal(Ids.Length, checks.Count);
        var project = checks.Single(c => c.GetProperty("id").GetString() == "project");
        Assert.Equal("fail", project.GetProperty("status").GetString());
        Assert.StartsWith("Winshell cannot read the config folder", project.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    private ChildProcess Run(params string[] args)
    {
        return RunIn(_root, args);
    }

    /// <summary>Runs winshell.exe with the config folder overridden to a folder of this class's.</summary>
    private static ChildProcess RunIn(string configRoot, params string[] args)
    {
        return WinshellCli.Run(args, (ConfigPaths.OverrideVariable, configRoot));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }
}
