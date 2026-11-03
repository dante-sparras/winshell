using System.Collections.Frozen;
using System.Globalization;

namespace Winshell;

internal static class CliParser
{
    internal static CliOptions Parse(string[] args)
    {
        args = WithoutAfter(args, out var after);
        var command = FirstCommand(args);
        if (command is not null && !Commands.ContainsKey(command))
            throw UnknownCommand(command);
        // Help wins over the rest of validation and execution. Arguments after -- or an IPC function are data.
        if (HelpRequested(args))
            return new CliOptions { Command = command, ShowHelp = true };

        var rest = command is null ? args : WithoutToken(args, command);
        var options = ParseOptions(rest, command);
        var positional = options.Positional;
        switch (command)
        {
            // help takes a command and log takes one file.
            case "help" or "log" when positional.Length > 1:
                throw Unexpected(positional[1]);
            // help <command> is <command> --help.
            case "help":
                var topic = positional.FirstOrDefault();
                if (topic is not null && !Commands.ContainsKey(topic))
                    throw UnknownCommand(topic);
                return new CliOptions { Command = topic, ShowHelp = true };
            case "log":
                break;
            // ipc and inspect check the words after their first themselves.
            case "ipc" or "inspect":
                if (positional.Length > 0 && !Subcommands(command).Contains(positional[0]))
                    throw new ArgumentException($"Unknown {command} command \"{positional[0]}\". Run winshell {command} --help.");
                break;
            default:
                if (positional.Length > 0)
                    throw Unexpected(positional[0]);
                break;
        }
        return options with
        {
            Command = command,
            After = after,
            Arguments = [.. args],
            ShowVersion = VersionRequested(args),
            ShellStartAllowed = OnlyShellStartOptions(args),
        };
    }

    private static readonly string[] Instance = ["--id", "--pid", "--path", "--config"];

    /// <summary>
    /// The cases <see cref="Cli.RunAsync"/> runs, and the options each takes by its long name. Every command also
    /// takes <c>-v</c>, <c>-h</c> and <c>-V</c>.
    /// </summary>
    private static readonly FrozenDictionary<string, string[]> Commands = new Dictionary<string, string[]>
    {
        ["run"] = ["--path", "--config", "--daemonize", "--no-duplicate", "--no-build"],
        ["list"] = ["--json"],
        ["kill"] = [.. Instance, "--force"],
        ["reload"] = Instance,
        ["log"] = [.. Instance, "--tail", "--follow"],
        ["ipc"] = [.. Instance, "--json"],
        ["inspect"] = Instance,
        ["dev"] = ["--path", "--config"],
        ["doctor"] = ["--path", "--config", "--json", "--no-color"],
        ["help"] = [],
    }.ToFrozenDictionary();

    private static bool Takes(string command, string option) =>
        option is "--verbose" or "--help" or "--version" || Commands[command].Contains(option);

    /// <summary>The long name of an option winshell has, such as <c>--path</c> for <c>-p</c>; null for any other word.</summary>
    private static string? LongName(string option) => option switch
    {
        "-p" or "--path" => "--path",
        "-c" or "--config" => "--config",
        "-i" or "--id" => "--id",
        "-d" or "--daemonize" => "--daemonize",
        "-n" or "--no-duplicate" => "--no-duplicate",
        "-v" or "--verbose" => "--verbose",
        "-j" or "--json" => "--json",
        "-f" or "--follow" => "--follow",
        "-t" or "--tail" => "--tail",
        "-V" or "--version" => "--version",
        "-h" or "--help" => "--help",
        "--pid" or "--force" or "--no-color" or "--no-build" => option,
        _ => null,
    };

    /// <summary>The first words <see cref="Winshell.Commands.IpcCommand"/> and <see cref="Winshell.Commands.InspectCommand"/> take.</summary>
    private static string[] Subcommands(string command) => command switch
    {
        "ipc" => ["show", "call", "prop", "wait", "listen"],
        "inspect" => ["tree", "screenshot", "click", "hover"],
        _ => [],
    };

    private static ArgumentException UnknownCommand(string command) =>
        new($"Unknown command \"{command}\". Run winshell --help.");

    private static ArgumentException Unexpected(string argument) => new($"Unexpected argument \"{argument}\".");

    /// <summary>The first word that is not an option. Options may come before it, so <c>-c work kill</c> is kill.</summary>
    internal static string? FirstCommand(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            // Words after -- are arguments, not a command.
            if (token == "--")
                return null;
            if (token is "-h" or "--help" or "/?" or "-V" or "--version")
                continue;
            if (token.StartsWith('-'))
            {
                if (TakesValue(token))
                    i++;
                continue;
            }
            return token;
        }
        return null;
    }

    private static string[] WithoutToken(string[] args, string command)
    {
        var skipped = false;
        var rest = new List<string>(args.Length);
        foreach (var token in args)
        {
            if (!skipped && token == command)
            {
                skipped = true;
                continue;
            }
            rest.Add(token);
        }
        return [.. rest];
    }

    private static bool TakesValue(string option) => LongName(option) is "--path" or "--config" or "--id" or "--pid" or "--tail";

    /// <summary>The Start tile and a sign-in entry pass these, and no command word.</summary>
    private static bool OnlyShellStartOptions(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (token == "--")
                return true;
            if (LongName(token) is not { } name || !Takes("run", name))
                return false;
            if (TakesValue(token))
                i++;
        }
        return true;
    }

    /// <summary>A config name is one folder in the config folder, so it can't leave it. A path goes in -p.</summary>
    private static string ConfigName(string option, string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException($"{option} needs the name of a config folder, not \"{name}\". Use -p for a path.");
        return name;
    }

    /// <summary>
    /// Reads options. <paramref name="command"/> is the word <see cref="FirstCommand"/> already took out of
    /// <paramref name="args"/>. A <c>--</c> ends options. So does the function name of <c>ipc call</c>:
    /// hotkeys pass <c>-v</c> and <c>-p</c> as arguments, and those must not become winshell options.
    /// </summary>
    internal static CliOptions ParseOptions(string[] args, string? command = null)
    {
        var options = new CliOptions();
        var positional = new List<string>();
        var rest = new List<string>();
        var raw = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (raw)
            {
                positional.Add(args[i]);
                continue;
            }
            if (args[i] == "--")
            {
                if (command is null or "run")
                {
                    rest.AddRange(args.Skip(i));
                    break;
                }
                raw = true;
                continue;
            }
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            var name = LongName(args[i]);
            if (name is not null && command is not null && !Takes(command, name))
                throw new ArgumentException($"winshell {command} does not take \"{args[i]}\".");
            switch (name)
            {
                case "--path":
                    options = options with { Path = Next() };
                    break;
                case "--config":
                    options = options with { Config = ConfigName(args[i], Next()) };
                    break;
                case "--id":
                    options = options with { Id = Next() };
                    break;
                case "--pid":
                    options = options with { Pid = WholeNumber(args[i], Next()) };
                    break;
                case "--daemonize":
                    options = options with { Daemonize = true };
                    break;
                case "--no-duplicate":
                    options = options with { NoDuplicate = true };
                    break;
                case "--verbose":
                    options = options with { Verbose = true };
                    break;
                case "--json":
                    options = options with { Json = true };
                    break;
                case "--follow":
                    options = options with { Follow = true };
                    break;
                case "--force":
                    options = options with { Force = true };
                    break;
                case "--tail":
                    options = options with { Tail = WholeNumber(args[i], Next()) };
                    break;
                case "--no-color":
                    options = options with { NoColor = true };
                    break;
                case "--no-build":
                    options = options with { NoBuild = true };
                    break;
                case "--version" or "--help":
                    break;
                default:
                    if (args[i].StartsWith('-'))
                        throw new ArgumentException($"Unknown option \"{args[i]}\".");
                    positional.Add(args[i]);
                    if (IpcCallArguments(command, positional))
                        raw = true;
                    break;
            }
        }
        return options with { Positional = [.. positional], Rest = [.. rest] };
    }

    /// <summary>
    /// True when <paramref name="positional"/> already holds the function name. Later words belong to the call.
    /// The positionals are what follows <paramref name="command"/>.
    /// </summary>
    private static bool IpcCallArguments(string? command, List<string> positional) =>
        command == "ipc" && positional.Count >= 3 && positional[0] == "call";

    private static readonly string[] HelpFlags = ["-h", "--help", "/?"];

    private static readonly string[] VersionFlags = ["-V", "--version"];

    /// <summary>True when help was asked for as an option, before <c>--</c> and before ipc call arguments.</summary>
    internal static bool HelpRequested(string[] args) => OptionBeforeEnd(args, HelpFlags);

    /// <summary>True when <c>-V</c> or <c>--version</c> is an option. After <c>--</c> it is an argument.</summary>
    internal static bool VersionRequested(string[] args) => OptionBeforeEnd(args, VersionFlags);

    private static bool OptionBeforeEnd(string[] args, string[] names)
    {
        var end = OptionRegionLength(args);
        for (var i = 0; i < end; i++)
        {
            if (Array.IndexOf(names, args[i]) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// How far options extend. <c>--</c> ends them, and so does the first argument of <c>ipc call</c>.
    /// </summary>
    private static int OptionRegionLength(string[] args)
    {
        string? command = null;
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (token == "--" || IpcCallArguments(command, positional))
                return i;
            if (token.StartsWith('-'))
            {
                if (TakesValue(token))
                    i++;
                continue;
            }
            if (command is null)
                command = token;
            else
                positional.Add(token);
        }
        return args.Length;
    }

    /// <summary>
    /// A process id or a tail count. A value that isn't one is an <see cref="ArgumentException"/>,
    /// which <see cref="Cli.RunAsync"/> turns into exit code 2.
    /// </summary>
    private static int WholeNumber(string option, string text)
    {
        // NumberStyles.Integer keeps "+5", surrounding whitespace and "-0" (which is 0). A negative is not a whole number.
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new ArgumentException($"{option} needs a whole number");
        return value;
    }

    /// <summary>
    /// Takes <c>--after &lt;pid&gt;</c> out of the options. Only an update's restart passes it, and it is not one
    /// of the arguments the shell was started with: a restart or a sign-in entry must not repeat it. It stays in
    /// place after <c>--</c> and after an <c>ipc call</c> function name, where it is an argument.
    /// </summary>
    internal static string[] WithoutAfter(string[] args, out int? after)
    {
        after = null;
        var region = OptionRegionLength(args);
        var i = Array.IndexOf(args, "--after", 0, region);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            return args;
        after = pid;
        return [.. args[..i], .. args[(i + 2)..]];
    }
}
