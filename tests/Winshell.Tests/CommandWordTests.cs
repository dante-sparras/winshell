namespace Winshell.Tests;

/// <summary>
/// A word winshell does not know as a command, or as the first word of <c>ipc</c> or <c>inspect</c>, is named in the
/// error, whatever follows it. These stay inside <see cref="CliParser"/> so a live instance on this PC is never called.
/// </summary>
public class CommandWordTests
{
    [Theory]
    [InlineData("foo bar", "foo")]
    [InlineData("-v foo bar", "foo")]
    [InlineData("msg notes add", "msg")]
    [InlineData("help foo", "foo")]
    public void An_unknown_command_is_named_even_with_words_after_it(string line, string word)
    {
        var e = Assert.Throws<ArgumentException>(() => CliParser.Parse(line.Split(' ')));

        Assert.Equal($"Unknown command \"{word}\". Run winshell --help.", e.Message);
    }

    [Theory]
    [InlineData("ipc bogus", "Unknown ipc command \"bogus\". Run winshell ipc --help.")]
    [InlineData("ipc bogus target func", "Unknown ipc command \"bogus\". Run winshell ipc --help.")]
    [InlineData("inspect bogus", "Unknown inspect command \"bogus\". Run winshell inspect --help.")]
    public void An_unknown_subcommand_is_named(string line, string message)
    {
        var e = Assert.Throws<ArgumentException>(() => CliParser.Parse(line.Split(' ')));

        Assert.Equal(message, e.Message);
    }

    [Theory]
    [InlineData("help kill", "kill")]
    [InlineData("help ipc", "ipc")]
    [InlineData("help", null)]
    public void Help_with_a_command_is_that_command_s_help(string line, string? command)
    {
        var options = CliParser.Parse(line.Split(' '));

        Assert.True(options.ShowHelp);
        Assert.Equal(command, options.Command);
    }

    [Fact]
    public void Help_takes_one_command()
    {
        var e = Assert.Throws<ArgumentException>(() => CliParser.Parse(["help", "kill", "log"]));

        Assert.Equal("Unexpected argument \"log\".", e.Message);
    }
}
