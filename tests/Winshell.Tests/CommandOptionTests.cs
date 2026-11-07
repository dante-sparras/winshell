using System.Globalization;

namespace Winshell.Tests;

/// <summary>
/// A command takes the options it names, and refuses the rest, so an option never does nothing. These stay inside
/// <see cref="CliParser"/> so a live instance on this PC is never called.
/// </summary>
public class CommandOptionTests
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

    // '' stands for an empty argument.
    [Theory]
    [InlineData("run -p missing --pth", "Unknown option \"--pth\".")]
    [InlineData("run -p missing --log-times", "Unknown option \"--log-times\".")]
    [InlineData("run -p missing --any-display", "Unknown option \"--any-display\".")]
    [InlineData("run -p missing --newest", "Unknown option \"--newest\".")]
    [InlineData("run -p missing --log-rules x", "Unknown option \"--log-rules\".")]
    [InlineData("run -p missing -m MyShell.exe", "Unknown option \"-m\".")]
    [InlineData("run -p missing --manifest MyShell.exe", "Unknown option \"--manifest\".")]
    [InlineData("run -p missing --debug 1", "Unknown option \"--debug\".")]
    [InlineData("list -a", "Unknown option \"-a\".")]
    [InlineData("list --all", "Unknown option \"--all\".")]
    [InlineData("reload --pid 0 --hard", "Unknown option \"--hard\".")]
    [InlineData("list extra", "Unexpected argument \"extra\".")]
    [InlineData("kill --pid 0 extra", "Unexpected argument \"extra\".")]
    [InlineData("run -p missing extra", "Unexpected argument \"extra\".")]
    [InlineData("doctor extra", "Unexpected argument \"extra\".")]
    [InlineData("log one two", "Unexpected argument \"two\".")]
    [InlineData("kill --pid abc", "--pid needs a whole number.")]
    [InlineData("kill --pid 1.5", "--pid needs a whole number.")]
    [InlineData("kill --pid 999999999999", "--pid needs a whole number.")]
    [InlineData("kill --pid -1", "--pid needs a whole number.")]
    [InlineData("kill --pid ''", "--pid needs a whole number.")]
    [InlineData("log --tail ten", "--tail needs a whole number.")]
    [InlineData("log --tail -5", "--tail needs a whole number.")]
    [InlineData("log -t ten", "-t needs a whole number.")]
    [InlineData("kill --pid", "--pid needs a value.")]
    [InlineData("log --tail", "--tail needs a value.")]
    public void A_bad_option_argument_or_number_is_named(string line, string message)
    {
        var args = line.Split(' ').Select(word => word == "''" ? "" : word).ToArray();

        var e = Assert.Throws<ArgumentException>(() => CliParser.Parse(args));

        Assert.Equal(message, e.Message);
    }

    [Theory]
    [InlineData("run -p dir", "path", "dir")]
    [InlineData("run -c work", "config", "work")]
    [InlineData("run -d", "daemonize", "")]
    [InlineData("run -n", "no-duplicate", "")]
    [InlineData("run --no-build", "no-build", "")]
    [InlineData("list -j", "json", "")]
    [InlineData("kill --force", "force", "")]
    [InlineData("kill -i ab", "id", "ab")]
    [InlineData("kill --pid 4", "pid", "4")]
    [InlineData("reload -c work", "config", "work")]
    [InlineData("log -t 2", "tail", "2")]
    [InlineData("log -f", "follow", "")]
    [InlineData("ipc show -j", "json", "")]
    [InlineData("inspect tree -i ab", "id", "ab")]
    [InlineData("dev -p dir", "path", "dir")]
    [InlineData("doctor --no-color", "no-color", "")]
    [InlineData("doctor -j", "json", "")]
    public void A_command_reads_the_options_it_accepts(string line, string field, string value)
    {
        var parsed = CliParser.Parse(line.Split(' '));

        Assert.Equal(line.Split(' ')[0], parsed.Command);
        Assert.Equal(value, Field(parsed, field));
    }

    [Fact]
    public void An_option_before_the_command_is_that_command_s()
    {
        var parsed = CliParser.Parse(["--pid", "4", "kill"]);

        Assert.Equal("kill", parsed.Command);
        Assert.Equal("4", Field(parsed, "pid"));
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
    private static string[] Words(string command)
    {
        return command switch
        {
            "ipc" => ["show"],
            "inspect" => ["tree"],
            _ => []
        };
    }

    private static string Field(CliOptions options, string field)
    {
        return field switch
        {
            "path" => options.Path ?? "",
            "config" => options.Config ?? "",
            "id" => options.Id ?? "",
            "pid" => options.Pid?.ToString(CultureInfo.InvariantCulture) ?? "",
            "tail" => options.Tail?.ToString(CultureInfo.InvariantCulture) ?? "",
            "daemonize" => options.Daemonize ? "" : "no",
            "no-duplicate" => options.NoDuplicate ? "" : "no",
            "no-build" => options.NoBuild ? "" : "no",
            "json" => options.Json ? "" : "no",
            "force" => options.Force ? "" : "no",
            "follow" => options.Follow ? "" : "no",
            "no-color" => options.NoColor ? "" : "no",
            _ => throw new ArgumentException(field)
        };
    }
}
