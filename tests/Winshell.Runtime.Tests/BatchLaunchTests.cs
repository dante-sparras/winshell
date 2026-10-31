using System.Diagnostics;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public sealed class BatchLaunchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public BatchLaunchTests()
    {
        Directory.CreateDirectory(_dir);
        // A sibling the old cmd /c line would have run as a second command.
        File.WriteAllText(Path.Combine(_dir, "b.cmd"), "@echo ran> \"%~dp0b.txt\"\r\n");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_script_path_with_an_ampersand_runs_only_that_script()
    {
        var script = Path.Combine(_dir, "a&b.cmd");
        File.WriteAllText(script, "@echo ran> \"%~dp0a.txt\"\r\n");

        Run([script]);

        Assert.True(File.Exists(Path.Combine(_dir, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "b.txt")));
    }

    [Fact]
    public void An_argument_with_cmd_operators_reaches_the_script_as_written()
    {
        var script = Path.Combine(_dir, "args.cmd");
        File.WriteAllText(script, "@echo %1 %2> \"%~dp0args.txt\"\r\n");

        Run([script, "plain", "x&b.cmd|<>()"]);

        Assert.Equal("plain \"x&b.cmd|<>()\"", File.ReadAllText(Path.Combine(_dir, "args.txt")).TrimEnd());
        Assert.False(File.Exists(Path.Combine(_dir, "b.txt")));
    }

    [Theory]
    [InlineData("%PATH%")]
    [InlineData("say \"hi\" & b.cmd")]
    [InlineData("!PATH!")]
    [InlineData("^&b.cmd")]
    [InlineData("one\r\nb.cmd")]
    public void An_argument_cmd_would_still_read_is_refused(string arg)
    {
        var script = Path.Combine(_dir, "args.cmd");
        File.WriteAllText(script, "@echo %1> \"%~dp0args.txt\"\r\n");

        Assert.Throws<ArgumentException>(() => ProcessLauncher.CreateStartInfo(Spec([script, arg]), redirect: false));
    }

    [Fact]
    public void A_detached_start_that_is_refused_is_logged()
    {
        var script = Path.Combine(_dir, "100%.cmd");
        File.WriteAllText(script, "@echo ran> \"%~dp0a.txt\"\r\n");
        var messages = new List<string>();

        ProcessLauncher.StartDetached(Spec([script]), new Log { Sink = (_, _, message) => messages.Add(message) });

        Assert.Contains(messages, m => m.StartsWith("Failed to start", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_dir, "a.txt")));
    }

    [Fact]
    public void A_detached_batch_start_gives_the_shell_cmd_and_the_safe_line()
    {
        var script = Path.Combine(_dir, "a&b.cmd");
        File.WriteAllText(script, "@echo ran> \"%~dp0a.txt\"\r\n");

        var plan = ProcessLauncher.PlanDetached(Spec([script, "x&b.cmd"]));

        Assert.True(plan.UseShellExecute);
        Assert.Equal(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", plan.File);
        Assert.Equal($"/d /s /c \"\"{script}\" \"x&b.cmd\"\"", plan.Arguments);
    }

    [Fact]
    public void A_detached_batch_start_cmd_would_read_is_refused_before_the_shell()
    {
        var script = Path.Combine(_dir, "args.cmd");
        File.WriteAllText(script, "@echo %1> \"%~dp0args.txt\"\r\n");

        Assert.Throws<ArgumentException>(() => ProcessLauncher.PlanDetached(Spec([script, "%PATH%"])));
    }

    [Fact]
    public void A_new_console_gets_the_batch_line_as_built()
    {
        var script = Path.Combine(_dir, "a&b.cmd");
        File.WriteAllText(script, "@echo ran> \"%~dp0a.txt\"\r\n");
        var spec = new ProcessSpec([script, "x&b.cmd"], new Dictionary<string, string?> { ["WINSHELL_BATCH_TEST"] = "1" }, false, _dir);
        var psi = ProcessLauncher.CreateStartInfo(spec, redirect: false);

        Assert.EndsWith($" /d /s /c \"\"{script}\" \"x&b.cmd\"\"", ProcessLauncher.CommandLine(psi), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_console_quotes_a_program_and_its_arguments()
    {
        var psi = new ProcessStartInfo(@"C:\My Tools\tool.exe", ["my file.txt", "plain"]);

        Assert.Equal(@"""C:\My Tools\tool.exe"" ""my file.txt"" plain", ProcessLauncher.CommandLine(psi));
    }

    private ProcessSpec Spec(string[] command) => new(command, new Dictionary<string, string?>(), false, _dir);

    private void Run(string[] command)
    {
        using var process = Process.Start(ProcessLauncher.CreateStartInfo(Spec(command), redirect: false))!;
        Assert.True(process.WaitForExit(10_000));
    }
}
