using System.Diagnostics;

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
        var (code, output, error) = Run("kill", "-p", "");

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.StartsWith("winshell kill:", error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Verbose_includes_the_stack()
    {
        var (code, _, error) = Run("-v", "kill", "-p", "");

        Assert.Equal(1, code);
        Assert.Contains("winshell kill:", error, StringComparison.Ordinal);
        Assert.Contains("   at ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_running_list_says_how_to_start_one_and_kill_does_not()
    {
        var local = ("WINSHELL_LOCAL_DIR", Path.Combine(Path.GetTempPath(), "winshell-cli-error-absent-local"));
        var list = WinshellCli.Run(["list"], local);
        var kill = WinshellCli.Run(["kill"], local);

        Assert.Equal((0, "No running instances. Start one with winshell run." + Environment.NewLine, ""), list);
        Assert.Equal((1, "", "No running instances." + Environment.NewLine), kill);
    }

    private static (int Code, string Output, string Error) Run(params string[] args) =>
        WinshellCli.Run(
            args,
            ("WINSHELL_CONFIG_DIR", Path.Combine(Path.GetTempPath(), "winshell-cli-error-absent")));
}
