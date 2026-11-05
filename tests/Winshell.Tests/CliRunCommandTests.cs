using System.Diagnostics;

namespace Winshell.Tests;

/// <summary>Starting a shell is <c>winshell run</c>. A bare <c>winshell</c> prints help, and options may precede the command.</summary>
[Trait("Category", "Slow")]
public class CliRunCommandTests
{
    [Fact]
    public void No_arguments_print_help_and_do_not_start_a_shell()
    {
        var (code, output, error) = Run([]);

        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("winshell run", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_before_the_command_do_not_start_a_shell()
    {
        // An unknown command, not kill: kill would stop a real instance on this PC.
        var (code, output, error) = Run(["-v", "nosuch"]);

        Assert.Equal(2, code);
        Assert.Equal("", output);
        Assert.Equal("Unknown command \"nosuch\". Run winshell --help." + Environment.NewLine, error);
    }

    [Fact]
    public void A_pid_option_before_kill_selects_that_process()
    {
        var (code, output, error) = Run(["--pid", "0", "kill"]);

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.Equal("No instance with process id 0." + Environment.NewLine, error);
    }

    [Fact]
    public void Run_with_a_missing_path_fails_before_a_shell_starts()
    {
        var missing = Path.Combine(Path.GetTempPath(), "winshell-cli-run-missing-" + Guid.NewGuid().ToString("N"));
        var (code, output, error) = Run(["run", "-p", missing]);

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.Contains("No .csproj", error, StringComparison.Ordinal);
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
            var (code, output, error) = WinshellCli.Run(args.Split(' '), ("WINSHELL_CONFIG_DIR", config), ("WINSHELL_LOCAL_DIR", local));

            Assert.Equal((0, "", ""), (code, output, error));
            var listed = WinshellCli.Run(["list", "--json"], ("WINSHELL_CONFIG_DIR", config), ("WINSHELL_LOCAL_DIR", local));
            Assert.Equal((0, "[]"), (listed.Code, listed.Output.Trim()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
            File.WriteAllText(Path.Combine(dir, "instance.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = "runningabc",
                ["pid"] = self.Id,
                ["shellId"] = "bar",
                ["configPath"] = Path.Combine(project, "bin", "shell.next", "Bar.exe"),
                ["launchTime"] = DateTime.Now,
                ["pipe"] = "winshell-runningabc",
                ["log"] = Path.Combine(dir, "log.txt"),
                ["processName"] = self.ProcessName,
                ["processStart"] = self.StartTime.ToUniversalTime().Ticks,
            }));

            var (code, output, error) = WinshellCli.Run(["run", "-n", "-p", project], ("WINSHELL_LOCAL_DIR", root));

            Assert.Equal(0, code);
            Assert.Equal("", output);
            Assert.Equal("", error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int Code, string Output, string Error) Run(string[] args) =>
        WinshellCli.Run(
            args,
            ("WINSHELL_CONFIG_DIR", Path.Combine(Path.GetTempPath(), "winshell-cli-run-absent")));
}
