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
