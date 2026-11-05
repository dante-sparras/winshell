namespace Winshell.Tests;

/// <summary>
/// A command other than <c>run</c> prints one sentence when the environment fails, and exits 1.
/// The shell's own crash reporting is left alone. With nothing running, kill says so plainly and list says how to start one.
/// </summary>
[Trait("Category", "Slow")]
public class CliErrorTests
{
    [Fact]
    public void An_empty_path_on_kill_is_one_sentence()
    {
        var result = Run("kill", "-p", "");

        result.AssertExitCode(1);
        Assert.Equal("", result.Output);
        Assert.StartsWith("winshell kill:", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Verbose_includes_the_stack()
    {
        var result = Run("-v", "kill", "-p", "");

        result.AssertExitCode(1);
        Assert.Contains("winshell kill:", result.Error, StringComparison.Ordinal);
        Assert.Contains("   at ", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_running_list_says_how_to_start_one_and_kill_does_not()
    {
        var local = ("WINSHELL_LOCAL_DIR", Path.Combine(Path.GetTempPath(), "winshell-cli-error-absent-local"));
        var list = WinshellCli.Run(["list"], local);
        var kill = WinshellCli.Run(["kill"], local);

        Assert.Equal((0, "No running instances. Start one with winshell run." + Environment.NewLine, ""),
            (list.Code, list.Output, list.Error));
        Assert.Equal((1, "", "No running instances." + Environment.NewLine), (kill.Code, kill.Output, kill.Error));
    }

    private static ChildProcess Run(params string[] args)
    {
        return WinshellCli.Run(
            args,
            ("WINSHELL_CONFIG_DIR", Path.Combine(Path.GetTempPath(), "winshell-cli-error-absent")));
    }
}
