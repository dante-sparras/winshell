namespace Winshell.Tests;

/// <summary><c>--pid</c> and <c>--tail</c> that are not whole numbers exit 2, before any instance is touched.</summary>
[Trait("Category", "Slow")]
public class CliUsageTests
{
    [Theory]
    [InlineData("kill", "--pid", "abc", "--pid needs a whole number.")]
    [InlineData("kill", "--pid", "1.5", "--pid needs a whole number.")]
    [InlineData("kill", "--pid", "999999999999", "--pid needs a whole number.")]
    [InlineData("kill", "--pid", "-1", "--pid needs a whole number.")]
    [InlineData("kill", "--pid", "", "--pid needs a whole number.")]
    [InlineData("log", "--tail", "ten", "--tail needs a whole number.")]
    [InlineData("log", "--tail", "-5", "--tail needs a whole number.")]
    [InlineData("log", "-t", "ten", "-t needs a whole number.")]
    [InlineData("kill", "--pid", null, "--pid needs a value.")]
    [InlineData("log", "--tail", null, "--tail needs a value.")]
    public void A_bad_number_exits_2_with_a_message(string command, string option, string? value, string message)
    {
        var args = value is null ? new[] { command, option } : new[] { command, option, value };
        var result = Run(args);

        result.AssertExitCode(2);
        Assert.Equal("", result.Output);
        Assert.Equal(message + Environment.NewLine, result.Error);
    }

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
