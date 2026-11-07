namespace Winshell.Tests;

/// <summary>A whole-number <c>--pid</c> that is no instance exits 1, and the largest <c>--tail</c> prints the file.</summary>
[Trait("Category", "Slow")]
public class CliUsageTests
{
    [Fact]
    public void A_whole_number_pid_that_is_not_an_instance_exits_1()
    {
        var result = Run(["kill", "--pid", "0"]);

        result.AssertExitCode(1);
        Assert.Equal("", result.Output);
        Assert.Equal("No instance with process id 0." + Environment.NewLine, result.Error);
    }

    [Fact]
    public void Tail_of_int_max_prints_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "winshell-cli-usage-" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(path, "one\r\ntwo\r\n");
        try
        {
            var result = Run(["log", path, "--tail", "2147483647"]);

            result.AssertExitCode(0);
            Assert.Equal("", result.Error);
            Assert.Equal("one" + Environment.NewLine + "two" + Environment.NewLine, result.Output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ChildProcess Run(string[] args)
    {
        return WinshellCli.Run(
            args,
            ("WINSHELL_CONFIG_DIR", Path.Combine(Path.GetTempPath(), "winshell-cli-usage-absent")));
    }
}
