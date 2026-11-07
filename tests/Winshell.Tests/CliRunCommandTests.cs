using System.Diagnostics;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>Starting a shell is <c>winshell run</c>, and a bare <c>winshell</c> prints help.</summary>
[Trait("Category", "Slow")]
public class CliRunCommandTests
{
    [Fact]
    public void No_arguments_print_help_and_do_not_start_a_shell()
    {
        var result = Run([]);

        result.AssertExitCode(0);
        Assert.Equal("", result.Error);
        Assert.Contains("winshell run", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_with_a_missing_path_fails_before_a_shell_starts()
    {
        var missing = Path.Combine(Path.GetTempPath(), "winshell-cli-run-missing-" + Guid.NewGuid().ToString("N"));
        var result = Run(["run", "-p", missing]);

        result.AssertExitCode(1);
        Assert.Equal("", result.Output);
        Assert.Contains("No .csproj", result.Error, StringComparison.Ordinal);
    }

    /// <summary>The sign-in entry passes <c>--no-build --no-duplicate</c>, and the Start tile <c>--no-duplicate</c>.</summary>
    [Theory]
    [InlineData("run")]
    [InlineData("--no-build --no-duplicate")]
    [InlineData("--no-duplicate")]
    public void Run_with_no_configuration_exits_0_says_nothing_and_starts_nothing(string args)
    {
        var root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        var config = Path.Combine(root, "config");
        var local = Path.Combine(root, "local");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(local);
        try
        {
            var result = WinshellCli.Run(args.Split(' '), ("WINSHELL_CONFIG_DIR", config),
                ("WINSHELL_LOCAL_DIR", local));

            Assert.Equal((0, "", ""), (result.Code, result.Output, result.Error));
            var listed = WinshellCli.Run(["list", "--json"], ("WINSHELL_CONFIG_DIR", config),
                ("WINSHELL_LOCAL_DIR", local));
            listed.AssertExitCode(0);
            Assert.Equal("[]", listed.Output.Trim());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void No_duplicate_exits_quietly_before_building_when_the_project_already_runs()
    {
        var root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            // A project that cannot build: a run that reaches the build exits 1. Its shell runs from shell.next.
            var project = Path.Combine(root, "MyProject");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "Bar.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(project, "Program.cs"), "this is not C#");
            var dir = Path.Combine(root, "run", "runningabc");
            Directory.CreateDirectory(dir);
            using var self = Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(dir, "instance.json"), JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["id"] = "runningabc",
                    ["pid"] = self.Id,
                    ["shellId"] = "bar",
                    ["configPath"] = Path.Combine(project, "bin", "shell.next", "Bar.exe"),
                    ["launchTime"] = DateTime.Now,
                    ["pipe"] = "winshell-runningabc",
                    ["log"] = Path.Combine(dir, "log.txt"),
                    ["processName"] = self.ProcessName,
                    ["processStart"] = self.StartTime.ToUniversalTime().Ticks
                }));

            var result = WinshellCli.Run(["run", "-n", "-p", project], ("WINSHELL_LOCAL_DIR", root));

            result.AssertExitCode(0);
            Assert.Equal("", result.Output);
            Assert.Equal("", result.Error);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static ChildProcess Run(string[] args)
    {
        return WinshellCli.Run(
            args,
            ("WINSHELL_CONFIG_DIR", Path.Combine(Path.GetTempPath(), "winshell-cli-run-absent")));
    }
}
