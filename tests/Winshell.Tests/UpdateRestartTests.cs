namespace Winshell.Tests;

/// <summary>
/// A shell that restarts to update starts winshell.exe again with <c>--after &lt;pid&gt;</c>. That argument is for the
/// new process only: it never reaches the arguments a later restart or the sign-in entry repeats.
/// </summary>
public class UpdateRestartTests
{
    [Fact]
    public void After_is_taken_out_of_the_arguments_the_shell_runs_with()
    {
        var args = Cli.WithoutAfter(["-c", "bar", "--after", "4242", "-n"], out var after);
        Assert.Equal(4242, after);
        Assert.Equal(["-c", "bar", "-n"], args);
    }

    [Theory]
    [InlineData("-c bar")]
    [InlineData("--after")]
    [InlineData("--after soon")]
    public void Without_a_process_id_nothing_is_taken_out(string line)
    {
        var given = line.Split(' ');
        var args = Cli.WithoutAfter(given, out var after);
        Assert.Null(after);
        Assert.Equal(given, args);
    }
}
