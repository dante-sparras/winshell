using System.Globalization;

namespace Winshell;

internal static class CliParser
{
    internal static CliOptions Parse(string[] args)
    {
        args = WithoutAfter(args, out var after);
        var command = FirstCommand(args);
        // Help wins over validation and execution. Arguments after -- or an IPC function are data.
        if (HelpRequested(args))
            return new CliOptions { Command = command, ShowHelp = true };

        var rest = command is null ? args : WithoutToken(args, command);
        var options = ParseOptions(rest, command);
        // log takes one file; IPC and inspect validate their own positional arguments.
        if (command is "log")
        {
            if (options.Positional.Length > 1)
                throw new ArgumentException($"Unexpected argument \"{options.Positional[1]}\".");
        }
        else if (command is not ("ipc" or "msg" or "inspect") && options.Positional.Length > 0)
        {
            throw new ArgumentException($"Unexpected argument \"{options.Positional[0]}\".");
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

    private static bool TakesValue(string option) => option is
        "-p" or "--path" or "-c" or "--config" or "-i" or "--id" or "--pid" or "-t" or "--tail";

    /// <summary>The Start tile and a sign-in entry pass these, and no command word.</summary>
    private static bool OnlyShellStartOptions(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (token == "--")
                return true;
            if (token is not ("-p" or "--path" or "-c" or "--config" or "-n" or "--no-duplicate"
                or "-d" or "--daemonize" or "-v" or "--verbose" or "--no-build"))
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
    /// <paramref name="args"/>. A <c>--</c> ends options. So does the function name of <c>ipc call</c> and
    /// <c>msg</c>: hotkeys pass <c>-v</c> and <c>-p</c> as arguments, and those must not become winshell options.
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
            switch (args[i])
            {
                case "-p" or "--path":
                    options = options with { Path = Next() };
                    break;
                case "-c" or "--config":
                    options = options with { Config = ConfigName(args[i], Next()) };
                    break;
                case "-i" or "--id":
                    options = options with { Id = Next() };
                    break;
                case "--pid":
                    options = options with { Pid = WholeNumber(args[i], Next()) };
                    break;
                case "-d" or "--daemonize":
                    options = options with { Daemonize = true };
                    break;
                case "-n" or "--no-duplicate":
                    options = options with { NoDuplicate = true };
                    break;
                case "-v" or "--verbose":
                    options = options with { Verbose = true };
                    break;
                case "-j" or "--json":
                    options = options with { Json = true };
                    break;
                case "-f" or "--follow":
                    options = options with { Follow = true };
                    break;
                case "--force":
                    options = options with { Force = true };
                    break;
                case "-t" or "--tail":
                    options = options with { Tail = WholeNumber(args[i], Next()) };
                    break;
                case "--no-color":
                    options = options with { NoColor = true };
                    break;
                case "--no-build":
                    options = options with { NoBuild = true };
                    break;
                case "-V" or "--version" or "-h" or "--help":
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
    /// <paramref name="command"/> is <c>ipc</c> or <c>msg</c>; the positionals are what follows that word.
    /// </summary>
    private static bool IpcCallArguments(string? command, List<string> positional) =>
        (command == "ipc" && positional.Count >= 3 && positional[0] == "call")
        || (command == "msg" && positional.Count >= 2);

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
    /// How far options extend. <c>--</c> ends them, and so does the first argument of <c>ipc call</c> or <c>msg</c>.
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
    /// place after <c>--</c> and after an <c>ipc call</c> or <c>msg</c> function name, where it is an argument.
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
