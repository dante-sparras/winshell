using System.Text.RegularExpressions;
using Winshell.Commands;

namespace Winshell.Tests;

/// <summary>
/// A command takes the options its help names, and refuses the rest, so an option never does nothing. These stay inside
/// <see cref="CliParser"/> so a live instance on this PC is never called.
/// </summary>
public partial class CommandOptionTests
{
    [Theory]
    [InlineData("list --force", "winshell list does not take \"--force\".")]
    [InlineData("list -c work", "winshell list does not take \"-c\".")]
    [InlineData("kill -j", "winshell kill does not take \"-j\".")]
    [InlineData("-d kill", "winshell kill does not take \"-d\".")]
    [InlineData("reload --no-build", "winshell reload does not take \"--no-build\".")]
    [InlineData("inspect --force tree", "winshell inspect does not take \"--force\".")]
    [InlineData("dev -j", "winshell dev does not take \"-j\".")]
    [InlineData("doctor --pid 5", "winshell doctor does not take \"--pid\".")]
    [InlineData("run -j", "winshell run does not take \"-j\".")]
    [InlineData("help -j", "winshell help does not take \"-j\".")]
    public void An_option_the_command_does_not_take_is_named(string line, string message)
    {
        var e = Assert.Throws<ArgumentException>(() => CliParser.Parse(line.Split(' ')));

        Assert.Equal(message, e.Message);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("list")]
    [InlineData("kill")]
    [InlineData("reload")]
    [InlineData("log")]
    [InlineData("ipc")]
    [InlineData("inspect")]
    [InlineData("dev")]
    [InlineData("doctor")]
    public void Every_option_a_command_s_help_names_is_one_it_takes(string command)
    {
        var named = DocumentedOption().Matches(HelpCommand.Text(command))
            .GroupBy(m => m.Groups["name"].Value, m => m.Groups["value"].Success)
            .ToList();

        Assert.NotEmpty(named);
        foreach (var option in named)
        {
            string[] args = option.Any(hasValue => hasValue) ? [command, .. Words(command), option.Key, "1"] : [command, .. Words(command), option.Key];
            var parsed = CliParser.Parse(args);
            Assert.Equal(command, parsed.Command);
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("list")]
    [InlineData("kill")]
    [InlineData("reload")]
    [InlineData("log")]
    [InlineData("ipc")]
    [InlineData("inspect")]
    [InlineData("dev")]
    [InlineData("doctor")]
    public void Every_command_takes_verbose(string command)
    {
        Assert.True(CliParser.Parse([command, .. Words(command), "-v"]).Verbose);
    }

    /// <summary>What ipc and inspect need after them to be a whole command line.</summary>
    private static string[] Words(string command) => command switch
    {
        "ipc" => ["show"],
        "inspect" => ["tree"],
        _ => [],
    };

    /// <summary>An option in help text, such as <c>--force</c> or <c>-c &lt;name&gt;</c>. A <c>--</c> on its own is not one.</summary>
    [GeneratedRegex(@"(?<![\w<-])(?<name>--?[a-z][a-z-]*)(?<value> <\w+>)?")]
    private static partial Regex DocumentedOption();
}
