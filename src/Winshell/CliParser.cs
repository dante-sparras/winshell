using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace Winshell;

/// <summary>
/// One table says what every command accepts. <see cref="Parse"/> reads the arguments once, and <see cref="Help"/>
/// prints the same table.
/// </summary>
internal static class CliParser
{
    internal static CliOptions Parse(string[] args)
    {
        var line = new Line(args);
        line.Read();
        // Help and the version win over the rest of validation. An unknown command is already refused.
        if (line.Help)
            return new CliOptions { Command = line.Command, ShowHelp = true };
        if (line.Command is not (null or "help") && line.Version)
            return new CliOptions { Command = line.Command, ShowVersion = true };
        return Finish(line);
    }

    /// <summary>The help for <paramref name="command"/>, or the summary when that is null or has no help of its own.</summary>
    internal static string Help(string? command)
    {
        var text = command is not null && Commands.TryGetValue(command, out var entry) ? entry.Usage : Summary;
        return text.Replace("{version}", CliMetadata.Version, StringComparison.Ordinal)
            .Replace("{config-root}", ShellLocations.Current.ConfigRoot, StringComparison.Ordinal)
            .Replace("{config-override}", ShellLocations.ConfigVariable, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd();
    }

    private static readonly Option HelpOption = Option.Flag(new CommandWord("--help", "-h", "/?"), o => o);

    private static readonly Option VersionOption = Option.Flag(new CommandWord("--version", "-V"), o => o);

    private static readonly Option[] Shared =
    [
        Option.Flag(CommandWords.Verbose, o => o with { Verbose = true }), HelpOption, VersionOption
    ];

    private static readonly Option PathOption =
        Option.Value(CommandWords.Path, (o, use) => o with { Path = use.Value });

    private static readonly Option ConfigOption = Option.Value(CommandWords.Config,
        (o, use) => o with { Config = ConfigName(use.Token, use.Value!) });

    private static readonly Option DaemonizeOption =
        Option.Flag(CommandWords.Daemonize, o => o with { Daemonize = true });

    private static readonly Option JsonOption =
        Option.Flag(new CommandWord("--json", "-j"), o => o with { Json = true });

    private static readonly Option[] Instance =
    [
        Option.Value(new CommandWord("--id", "-i"), (o, use) => o with { Id = use.Value }),
        Option.Value(new CommandWord("--pid"), (o, use) => o with { Pid = WholeNumber(use.Token, use.Value!) }),
        PathOption, ConfigOption
    ];

    private const string RunUsage = """
                                    winshell run [-c <name> | -p <path>] [-d] [-n] [--no-build] [-- <args>...]
                                      Starts a configuration project's shell, built first when a source is newer than its
                                      program. With no project, nothing starts.
                                      -d, --daemonize        run in the background
                                      -n, --no-duplicate     exit quietly if this config is already running
                                          --no-build         run the last build, and do not build again
                                      Words after -- go to the configuration's program.
                                    """;

    private const string ListUsage = """
                                     winshell list [-j]
                                       Lists running instances. -j, --json prints them as JSON.
                                       Stopped instances are not listed.
                                     """;

    private const string KillUsage = """
                                     winshell kill [--force] [-i <id> | --pid <pid> | -c <name> | -p <path>]
                                       Asks one running instance to exit, then waits until that process has exited.
                                       --force ends the process when it does not exit.
                                       With one running instance and no selector, that instance is used.
                                       With several, this exits and lists them.
                                     """;

    private const string ReloadUsage = """
                                       winshell reload [-i <id> | --pid <pid> | -c <name> | -p <path>]
                                         Builds a configuration project and asks its instance to start again.
                                         A build that fails leaves the instance running and asks it to show the error.
                                         With one running instance and no selector, that instance is used.
                                         With several, this exits and lists them.
                                       """;

    private const string InspectUsage = """
                                        winshell inspect [-i <id> | --pid <pid> | -c <name> | -p <path>] <command>
                                          tree                            print each open window's live layout as JSON
                                          screenshot <file> [window]      save a window as a PNG
                                          click <name> [window]           activate an element, as Narrator does
                                          hover <name> [window]           put a control in its pointer look
                                          A window is the type of its content, or its number in tree. The first is the default.
                                          A name is an x:Name, or an automation id or name. Nothing goes through Windows' input.
                                        """;

    private const string DevUsage = """
                                    winshell dev [-c <name> | -p <path>]
                                      Runs a configuration project and applies each edit to the running shell, until Ctrl+C.
                                      A saved C# or XAML file reaches the shell in place, and the other windows are left alone.
                                      What does not compile shows on screen, and the last version that did keeps running.
                                      Needs the .NET SDK. See the guide for what an edit can change.
                                    """;

    private const string LogUsage = """
                                    winshell log [-i <id> | --pid <pid> | -c <name> | -p <path>] [-t <n>] [-f] [file]
                                      Prints an instance's log. -t, --tail <n> prints the last n lines.
                                      -f, --follow keeps printing new lines. winshell log <file> prints that file.
                                      With one running instance and no selector, that instance is used.
                                      With several, this exits and lists them.
                                      With nothing running and no selector, the log of a stopped instance is printed.
                                    """;

    private const string IpcUsage = """
                                    winshell ipc [-i <id> | --pid <pid> | -c <name> | -p <path>] <command>
                                      show [-j]                             list IPC targets. -j prints JSON
                                      call <target> <function> [args...]    call a function and print the result
                                      call notice dismiss                   close Winshell's own notice
                                      prop get <target> <property>          print a property
                                      wait <target> <signal>                wait for one signal
                                      listen <target> <signal>              print each signal until Ctrl+C
                                      Words after the function name are arguments, even when they start with -.
                                      A -- ends options on every command.
                                    """;

    private const string DoctorUsage = """
                                       winshell doctor [-c <name> | -p <path>] [-j] [--no-color]
                                         Checks this PC's setup for Winshell and changes nothing.
                                         -c, --config <name>    a named config in {config-root}
                                         -j, --json             print the checks as JSON
                                             --no-color         print without color
                                       """;

    private const string Summary = """
                                   winshell {version}: a desktop shell for Windows 11

                                   Usage: winshell run [options]              start a shell
                                          winshell                            print this help
                                          winshell <command> --help           help for that command, which does not run
                                          winshell list [-j]                  list running instances
                                          winshell kill [--force] [instance options]
                                                                              stop an instance and wait until it exits
                                          winshell reload [instance options]
                                                                              reload an instance's configuration
                                          winshell dev [-c <name> | -p <path>]
                                                                              run a project and apply each edit to it
                                          winshell log [-f] [-t N] [file]     print an instance's log
                                          winshell ipc show [-j]              list IPC targets
                                          winshell ipc call <target> <function> [args...]
                                          winshell ipc prop get <target> <property>
                                          winshell ipc wait <target> <signal>
                                          winshell ipc listen <target> <signal>
                                          winshell inspect tree | screenshot <file> | click <name> | hover <name>
                                                                              look at a running shell, or act on it
                                          winshell doctor [config options] [-j] [--no-color]
                                                                              check this PC's setup for Winshell
                                          winshell help [command]             print this summary, or that command's help

                                   Config selection:
                                     -p, --path <path>      a project file, or the folder it is in
                                     -c, --config <name>    a named config in {config-root}
                                                            ({config-override} overrides that folder)
                                   Instance selection (kill, reload, log, ipc, inspect):
                                     -i, --id <id>          an instance id, or the start of one
                                         --pid <pid>        an instance's process id
                                   Other:
                                     -d, --daemonize        run in the background
                                     -n, --no-duplicate     exit quietly if this config is already running
                                         --no-build         run the last build, and do not build again
                                     -v, --verbose          log debug messages
                                     -j, --json             print list, ipc show or doctor as JSON
                                         --no-color         print doctor without color
                                     -h, --help, /?         print help and exit
                                     -V, --version          print the version
                                     --                     end options; later words are the program's arguments

                                   Exit codes: 0 done, 1 the command failed, 2 the command line was wrong.
                                   """;

    /// <summary>Every command, the options and words it takes, and the help it prints. Each also takes <see cref="Shared"/>.</summary>
    private static readonly Command[] Entries =
    [
        Entry(CommandWords.Run, RunUsage, WordRule.None,
        [
            PathOption, ConfigOption, DaemonizeOption,
            Option.Flag(CommandWords.NoDuplicate, o => o with { NoDuplicate = true }),
            Option.Flag(CommandWords.NoBuild, o => o with { NoBuild = true })
        ]),
        Entry("list", ListUsage, WordRule.None, [JsonOption]),
        Entry("kill", KillUsage, WordRule.None,
            [.. Instance, Option.Flag(new CommandWord("--force"), o => o with { Force = true })]),
        Entry("reload", ReloadUsage, WordRule.None, Instance),
        Entry("log", LogUsage, WordRule.One,
        [
            .. Instance,
            Option.Value(new CommandWord("--tail", "-t"),
                (o, use) => o with { Tail = WholeNumber(use.Token, use.Value!) }),
            Option.Flag(new CommandWord("--follow", "-f"), o => o with { Follow = true })
        ]),
        Entry("ipc", IpcUsage, WordRule.Subcommand, [.. Instance, JsonOption],
        [
            new Subcommand("show"),
            new Subcommand("call", 2, "a target and a function", RawAfter: true),
            new Subcommand("prop", 3, "\"get\", a target and a property", "get"),
            new Subcommand("wait", 2, "a target and a signal"),
            new Subcommand("listen", 2, "a target and a signal")
        ]),
        Entry("inspect", InspectUsage, WordRule.Subcommand, Instance,
        [
            new Subcommand("tree"),
            new Subcommand("screenshot", 1, "a file"),
            new Subcommand("click", 1, "a name"),
            new Subcommand("hover", 1, "a name")
        ]),
        Entry("dev", DevUsage, WordRule.None, [PathOption, ConfigOption]),
        Entry("doctor", DoctorUsage, WordRule.None,
        [
            PathOption, ConfigOption, JsonOption,
            Option.Flag(new CommandWord("--no-color"), o => o with { NoColor = true })
        ]),
        Entry("help", Summary, WordRule.Topic, [])
    ];

    private static readonly FrozenDictionary<string, Command>
        Commands = Entries.ToFrozenDictionary(entry => entry.Name);

    /// <summary>
    /// <c>run</c>. A line with no command word and only its options starts a shell, as the Start tile and sign-in do.
    /// </summary>
    private static readonly Command RunEntry = Commands[CommandWords.Run];

    private static readonly FrozenDictionary<string, Option> ByToken = IndexTokens();

    private static FrozenDictionary<string, Option> IndexTokens()
    {
        var map = new Dictionary<string, Option>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        foreach (var option in entry.Options)
            option.Collect(map);
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static Command Entry(string name, string usage, WordRule words, Option[] options,
        Subcommand[]? subcommands = null)
    {
        return new Command(name, usage, [.. Shared, .. options], subcommands ?? [], words);
    }

    private static CliOptions Finish(Line line)
    {
        var options = Apply(line);
        var positional = line.Positional;
        if (line.Command is not { } name)
        {
            if (positional.Count > 0)
                throw Unexpected(positional[0]);
        }
        else
        {
            var command = Commands[name];
            switch (command.Words)
            {
                case WordRule.None:
                    if (positional.Count > 0)
                        throw Unexpected(positional[0]);
                    break;
                case WordRule.One:
                    if (positional.Count > 1)
                        throw Unexpected(positional[1]);
                    break;
                case WordRule.Topic:
                    if (positional.Count > 1)
                        throw Unexpected(positional[1]);
                    var topic = positional.FirstOrDefault();
                    if (topic is not null && !Commands.ContainsKey(topic))
                        throw UnknownCommand(topic);
                    return new CliOptions { Command = topic, ShowHelp = true };
                case WordRule.Subcommand:
                    RequireSubcommand(command, positional);
                    break;
            }
        }

        return options with
        {
            Command = line.Command,
            Arguments = [.. line.Args],
            ShowVersion = line.Version,
            ShellStartAllowed = line.Command is null && line.Uses.All(use => RunEntry.Accepts(use.Option)),
            Positional = [.. positional],
            Rest = [.. line.Rest],
            ChildArguments = BackgroundChild(line, options)
        };
    }

    /// <summary>
    /// What a <c>-d</c> child runs. The flag itself is gone. <c>--no-build</c> is added when this process builds,
    /// so the child does not build again. A value spelled <c>-d</c> stays.
    /// </summary>
    private static ImmutableArray<string> BackgroundChild(Line line, CliOptions options)
    {
        if (!options.Daemonize)
            return [];
        var child = line.Child.ToList();
        if (line.Command is null)
            child.Insert(0, CommandWords.Run);
        if (!options.NoBuild)
            child.Insert(0, CommandWords.NoBuild.Long);
        return [.. child];
    }

    private static CliOptions Apply(Line line)
    {
        var options = new CliOptions();
        ArgumentException? error = null;
        var command = line.Command is null ? null : Commands[line.Command];
        foreach (var use in line.Uses)
        {
            if (use.Option is null)
            {
                error ??= new ArgumentException($"Unknown option \"{use.Token}\".");
                continue;
            }

            if (command is not null && !command.Accepts(use.Option))
            {
                error ??= new ArgumentException($"winshell {command.Name} does not take \"{use.Token}\".");
                continue;
            }

            if (use.Missing)
            {
                error ??= new ArgumentException($"{use.Token} needs a value.");
                continue;
            }

            try
            {
                options = use.Option.Set(options, use);
            }
            catch (ArgumentException e)
            {
                error ??= e;
            }
        }

        if (error is not null)
            throw error;
        return options;
    }

    private static void RequireSubcommand(Command command, List<string> positional)
    {
        if (positional.Count == 0)
            throw new ArgumentException(
                $"winshell {command.Name} needs a command. Run winshell {command.Name} --help.");
        var name = positional[0];
        var match = command.Subcommands.FirstOrDefault(sub => sub.Name == name);
        if (match.Name is null)
            throw new ArgumentException(
                $"Unknown {command.Name} command \"{name}\". Run winshell {command.Name} --help.");
        var words = positional.Count - 1;
        var wrongWord = match.Word is { } required && (words < 1 || positional[1] != required);
        if (wrongWord || words < match.Words)
            throw new ArgumentException($"winshell {command.Name} {name} needs {match.Missing}.");
    }

    private static string ConfigName(string option, string name)
    {
        if (!CommandWords.IsConfigName(name))
            throw new ArgumentException(
                $"{option} needs the name of a config folder, not \"{name}\". Use -p for a path.");
        return name;
    }

    /// <summary>
    /// A process id or a tail count. A value that isn't one is an <see cref="ArgumentException"/>,
    /// which <see cref="Cli.RunAsync"/> turns into exit code 2.
    /// </summary>
    private static int WholeNumber(string option, string text)
    {
        // NumberStyles.Integer keeps "+5", surrounding whitespace and "-0" (which is 0). A negative is not a whole number.
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new ArgumentException($"{option} needs a whole number.");
        return value;
    }

    private static ArgumentException UnknownCommand(string command)
    {
        return new ArgumentException($"Unknown command \"{command}\". Run winshell --help.");
    }

    private static ArgumentException Unexpected(string argument)
    {
        return new ArgumentException($"Unexpected argument \"{argument}\".");
    }

    /// <summary>How many positional words a command takes after its name.</summary>
    private enum WordRule
    {
        None,
        One,
        Topic,
        Subcommand
    }

    /// <summary>An option's spellings, whether it takes a value, and what it sets.</summary>
    private sealed class Option(CommandWord word, bool takesValue, Func<CliOptions, Use, CliOptions> set)
    {
        public bool TakesValue => takesValue;

        public static Option Flag(CommandWord word, Func<CliOptions, CliOptions> set)
        {
            return new Option(word, false, (options, _) => set(options));
        }

        public static Option Value(CommandWord word, Func<CliOptions, Use, CliOptions> set)
        {
            return new Option(word, true, set);
        }

        public CliOptions Set(CliOptions options, Use use)
        {
            return set(options, use);
        }

        public void Collect(Dictionary<string, Option> into)
        {
            into.TryAdd(word.Long, this);
            foreach (var name in word.Shorts)
                into.TryAdd(name, this);
        }
    }

    /// <summary>
    /// A word of <c>ipc</c> or <c>inspect</c>. <see cref="Words"/> is how many words must follow it, and
    /// <see cref="Word"/> is one that must be first. <see cref="RawAfter"/> means the rest are that command's
    /// arguments, even when they look like options.
    /// </summary>
    private readonly record struct Subcommand(
        string Name,
        int Words = 0,
        string? Missing = null,
        string? Word = null,
        bool RawAfter = false);

    private sealed record Command(string Name, string Usage, Option[] Options, Subcommand[] Subcommands, WordRule Words)
    {
        public bool Accepts(Option? option)
        {
            return option is not null && Options.Contains(option);
        }
    }

    /// <summary>An option on the line. <see cref="Option"/> is null when no option is spelled <see cref="Token"/>.</summary>
    private readonly record struct Use(string Token, Option? Option, string? Value, bool Missing);

    /// <summary>One walk over the arguments. The command is known by the end, including when it comes after its options.</summary>
    private sealed class Line(string[] args)
    {
        public string[] Args { get; } = args;

        public string? Command { get; private set; }

        public bool Help { get; private set; }

        public bool Version { get; private set; }

        public List<Use> Uses { get; } = [];

        public List<string> Positional { get; } = [];

        public List<string> Rest { get; } = [];

        /// <summary>Every token except a daemonize flag. A <c>-d</c> that is some other option's value stays.</summary>
        public List<string> Child { get; } = [];

        private bool _raw;

        public void Read()
        {
            for (var i = 0; i < Args.Length; i++)
            {
                var token = Args[i];
                if (_raw)
                {
                    Positional.Add(token);
                    Child.Add(token);
                    continue;
                }

                if (token == CommandWords.End)
                {
                    // For run, and when there is no command, the rest belongs to the configuration's program.
                    if (Command is null or CommandWords.Run)
                    {
                        Rest.AddRange(Args.Skip(i));
                        Child.AddRange(Args.Skip(i));
                        return;
                    }

                    _raw = true;
                    continue;
                }

                if (ByToken.TryGetValue(token, out var option))
                {
                    string? value = null;
                    var missing = false;
                    if (option.TakesValue)
                    {
                        if (i + 1 >= Args.Length)
                            missing = true;
                        else
                            value = Args[++i];
                    }

                    Uses.Add(new Use(token, option, value, missing));
                    if (option != DaemonizeOption)
                    {
                        Child.Add(token);
                        if (value is not null)
                            Child.Add(value);
                    }

                    if (option == HelpOption)
                        Help = true;
                    else if (option == VersionOption)
                        Version = true;
                    continue;
                }

                if (token.StartsWith('-'))
                {
                    Uses.Add(new Use(token, null, null, false));
                    Child.Add(token);
                    continue;
                }

                if (Command is null)
                {
                    if (!Commands.ContainsKey(token))
                        throw UnknownCommand(token);
                    Command = token;
                    Child.Add(token);
                    continue;
                }

                Positional.Add(token);
                Child.Add(token);
                if (Command is { } name && Commands[name].Words == WordRule.Subcommand)
                {
                    var sub = Commands[name].Subcommands.FirstOrDefault(candidate => candidate.Name == Positional[0]);
                    if (sub.RawAfter && Positional.Count >= sub.Words + 1)
                        _raw = true;
                }
            }
        }
    }
}
