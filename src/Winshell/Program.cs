using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Winshell.DefaultShell;
using Winshell.Diagnostics;
using Winshell.Updates;

namespace Winshell
{
    internal static class Program
    {
        /// <summary>
        /// The shell's event loop runs on this thread, so it is a single-threaded apartment: shell objects and
        /// ShellExecute run on it directly, not in a hidden apartment through a proxy. A console app has no
        /// synchronization context, so the commands that await continue on the thread pool while this thread waits.
        /// </summary>
        [STAThread]
        private static int Main(string[] args) => Cli.Run(args).GetAwaiter().GetResult();
    }

    /// <summary>The command line.</summary>
    internal static class Cli
    {
        internal sealed class Options
        {
            public string? Path { get; set; }

            public string? Config { get; set; }

            public string? Id { get; set; }

            public int? Pid { get; set; }

            /// <summary>A shell that restarted to update: this one waits for that process to end first.</summary>
            public int? After { get; set; }

            public bool Daemonize { get; set; }

            public bool NoDuplicate { get; set; }

            public bool Verbose { get; set; }

            public bool Json { get; set; }

            public bool Follow { get; set; }

            /// <summary>End the process when the instance does not exit after it is asked.</summary>
            public bool Force { get; set; }

            public bool Hard { get; set; }

            /// <summary>Run the program the last build left. A failed build never starts that program unless this is set.</summary>
            public bool NoBuild { get; set; }

            public bool NoColor { get; set; }

            public int? Tail { get; set; }

            public List<string> Positional { get; } = [];

            /// <summary>The <c>--</c> and everything after it. Those words belong to the configuration's program.</summary>
            public List<string> Rest { get; } = [];
        }

        private static readonly string Version = typeof(Cli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        /// <summary>
        /// The GitHub repository a release build was published from, as <c>scripts/package.ps1</c> stamps it. A build
        /// from source has none, so it never updates itself.
        /// </summary>
        private static readonly string? ReleaseRepository = typeof(Cli).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "WinshellReleaseRepository")?.Value;

        /// <summary>
        /// A pipe or a file gets UTF-8. The console's own code page would turn text outside it into question marks,
        /// and setting that code page would change it for the terminal too.
        /// </summary>
        private static void WriteUtf8WhenRedirected()
        {
            var utf8 = new UTF8Encoding(false);
            if (Console.IsOutputRedirected)
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
            if (Console.IsErrorRedirected)
                Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        }

        public static async Task<int> Run(string[] args)
        {
            WriteUtf8WhenRedirected();
            args = WithoutAfter(args, out var after);
            // Help is checked before any command runs. `winshell kill --help` must not kill the shell.
            // A --, and the arguments of ipc call, end that scan so a help word there is data.
            if (HelpRequested(args))
            {
                PrintHelp(FirstCommand(args));
                return 0;
            }
            var command = FirstCommand(args);
            var rest = command is null ? args : WithoutToken(args, command);
            Options options;
            try
            {
                options = Parse(rest, command);
                options.After = after;
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                return 2;
            }
            // A spare word must not be ignored. log takes one file, and ipc takes its own arguments.
            if (command is "log")
            {
                if (options.Positional.Count > 1)
                {
                    Console.Error.WriteLine($"Unexpected argument \"{options.Positional[1]}\".");
                    return 2;
                }
            }
            else if (command is not ("ipc" or "msg") && options.Positional.Count > 0)
            {
                Console.Error.WriteLine($"Unexpected argument \"{options.Positional[0]}\".");
                return 2;
            }
            // list, kill, log, ipc and doctor can fail on the files and pipes around them. That is a sentence
            // and exit 1. RunShell reports a crash itself, so it is not caught here.
            try
            {
            switch (command)
            {
                case null:
                    if (VersionRequested(args))
                    {
                        Console.WriteLine($"winshell {Version}");
                        return 0;
                    }
                    // A console with no command prints help. winshellw, and a console that only passes the options
                    // a shell start uses, still starts: that is the Start tile, the sign-in entry, and the tests.
                    if (args.Length == 0 && !IsWindowlessHost())
                    {
                        PrintHelp(null);
                        return 0;
                    }
                    if (!IsWindowlessHost() && !OnlyShellStartOptions(args))
                    {
                        Console.Error.WriteLine("No command. Use winshell run to start a shell.");
                        return 2;
                    }
                    return RunShell(options, args);
                case "run":
                    return RunShell(options, args);
                case "list":
                    return List(options);
                case "kill":
                    return await Kill(options);
                case "reload":
                    return await Reload(options);
                case "log":
                    return await PrintLog(options);
                case "ipc":
                    return await Ipc(options);
                case "doctor":
                    return Doctor(options);
                case "msg":
                    Console.Error.WriteLine("winshell msg is deprecated; use winshell ipc call.");
                    options.Positional.Insert(0, "call");
                    return await Ipc(options);
                case "help":
                    PrintHelp(null);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown command \"{command}\". Run winshell --help.");
                    return 2;
            }
            }
            catch (Exception e) when (e is not OutOfMemoryException && command is not (null or "run"))
            {
                Console.Error.WriteLine(options.Verbose ? $"winshell {command}: {e}" : $"winshell {command}: {e.Message}");
                return 1;
            }
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

        /// <summary><c>winshellw.exe</c> is the same program with no console. It still starts a shell when given no command.</summary>
        private static bool IsWindowlessHost() =>
            string.Equals(System.IO.Path.GetFileName(Environment.ProcessPath), "winshellw.exe", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reads options. <paramref name="command"/> is the word <see cref="FirstCommand"/> already took out of
        /// <paramref name="args"/>. A <c>--</c> ends options. So does the function name of <c>ipc call</c> and
        /// <c>msg</c>: hotkeys pass <c>-v</c> and <c>-p</c> as arguments, and those must not become winshell options.
        /// </summary>
        /// <summary>A config name is one folder in the config folder, so it can't leave it. A path goes in -p.</summary>
        private static string ConfigName(string option, string name)
        {
            if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0)
                throw new ArgumentException($"{option} needs the name of a config folder, not \"{name}\". Use -p for a path.");
            return name;
        }

        internal static Options Parse(string[] args, string? command = null)
        {
            var o = new Options();
            var raw = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (raw)
                {
                    o.Positional.Add(args[i]);
                    continue;
                }
                if (args[i] == "--")
                {
                    if (command is null or "run")
                    {
                        o.Rest.AddRange(args.Skip(i));
                        break;
                    }
                    raw = true;
                    continue;
                }
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i])
                {
                    case "-p" or "--path":
                        o.Path = Next();
                        break;
                    case "-c" or "--config":
                        o.Config = ConfigName(args[i], Next());
                        break;
                    case "-i" or "--id":
                        o.Id = Next();
                        break;
                    case "--pid":
                        o.Pid = WholeNumber(args[i], Next());
                        break;
                    case "-d" or "--daemonize":
                        o.Daemonize = true;
                        break;
                    case "-n" or "--no-duplicate":
                        o.NoDuplicate = true;
                        break;
                    case "-v" or "--verbose":
                        o.Verbose = true;
                        break;
                    case "-j" or "--json":
                        o.Json = true;
                        break;
                    case "-f" or "--follow":
                        o.Follow = true;
                        break;
                    case "--hard":
                        o.Hard = true;
                        break;
                    case "--force":
                        o.Force = true;
                        break;
                    case "-t" or "--tail":
                        o.Tail = WholeNumber(args[i], Next());
                        break;
                    case "--no-color":
                        o.NoColor = true;
                        break;
                    case "--no-build":
                        o.NoBuild = true;
                        break;
                    case "-V" or "--version" or "-h" or "--help":
                        break;
                    default:
                        if (args[i].StartsWith('-'))
                            throw new ArgumentException($"Unknown option \"{args[i]}\".");
                        o.Positional.Add(args[i]);
                        if (IpcCallArguments(command, o.Positional))
                            raw = true;
                        break;
                }
            }
            return o;
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
        /// which <see cref="Run"/> turns into exit code 2.
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

        private static void PrintHelp(string? command)
        {
            switch (command)
            {
                case "list":
                    WriteHelp("""
                        winshell list [-j]
                          Lists running instances. -j, --json prints them as JSON.
                          Stopped instances are not listed.
                        """);
                    return;
                case "kill":
                    WriteHelp("""
                        winshell kill [--force] [-i <id> | --pid <pid> | -c <name> | -p <path>]
                          Asks one running instance to exit, then waits until that process has exited.
                          --force ends the process when it does not exit.
                          With one running instance and no selector, that instance is used.
                          With several, this exits and lists them.
                        """);
                    return;
                case "reload":
                    WriteHelp("""
                        winshell reload [--hard] [-i <id> | --pid <pid> | -c <name> | -p <path>]
                          Builds a configuration project and asks its instance to start again.
                          A build that fails leaves the instance running and asks it to show the error.
                          The default shell starts again. --hard is accepted.
                          With one running instance and no selector, that instance is used.
                          With several, this exits and lists them.
                        """);
                    return;
                case "log":
                    WriteHelp("""
                        winshell log [-i <id> | --pid <pid> | -c <name> | -p <path>] [-t <n>] [-f] [file]
                          Prints an instance's log. -t, --tail <n> prints the last n lines.
                          -f, --follow keeps printing new lines. winshell log <file> prints that file.
                          With one running instance and no selector, that instance is used.
                          With several, this exits and lists them.
                          With nothing running and no selector, the log of a stopped instance is printed.
                        """);
                    return;
                case "ipc":
                    WriteHelp("""
                        winshell ipc [-i <id> | --pid <pid> | -c <name> | -p <path>] <command>
                          show [-j]                             list IPC targets. -j prints JSON
                          call <target> <function> [args...]    call a function and print the result
                          call notice dismiss                   close Winshell's own notice
                          prop get <target> <property>          print a property
                          wait <target> <signal>                wait for one signal
                          listen <target> <signal>              print each signal until Ctrl+C
                          Words after the function name are arguments, even when they start with -.
                          A -- ends options on every command.
                        """);
                    return;
                case "doctor":
                    WriteHelp($"""
                        winshell doctor [-c <name> | -p <path>] [-j] [--no-color]
                          Checks this PC's setup for Winshell and changes nothing.
                          -c, --config <name>    a named config in {ConfigPaths.Root()}
                          -j, --json             print the checks as JSON
                              --no-color         print without color
                        """);
                    return;
                case "msg":
                    WriteHelp("""
                        winshell msg is the old name of winshell ipc call.
                        """);
                    return;
            }
            WriteHelp($"""
                winshell {Version}: a desktop shell for Windows 11

                Usage: winshell run [options]              start a shell
                       winshell                            print this help
                       winshell <command> --help           help for that command, which does not run
                       winshell list [-j]                  list running instances
                       winshell kill [--force] [instance options]
                                                           stop an instance and wait until it exits
                       winshell reload [--hard] [instance options]
                                                           reload an instance's configuration
                       winshell log [-f] [-t N] [file]     print an instance's log
                       winshell ipc show [-j]              list IPC targets
                       winshell ipc call <target> <function> [args...]
                       winshell ipc prop get <target> <property>
                       winshell ipc wait <target> <signal>
                       winshell ipc listen <target> <signal>
                       winshell doctor [config options] [-j] [--no-color]
                                                           check this PC's setup for Winshell
                       winshell help                       print this summary

                Config selection:
                  -p, --path <path>      a project file, or the folder it is in
                  -c, --config <name>    a named config in {ConfigPaths.Root()}
                                         ({ConfigPaths.OverrideVariable} overrides that folder)
                Instance selection (kill, reload, log, ipc):
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
                """);
        }

        private static void WriteHelp(string text) => Console.WriteLine(text.Replace("\r\n", "\n").TrimEnd());

        /// <summary>The project <c>run</c> would start, or null when the default shell runs. A reason means it cannot choose.</summary>
        private static ConfigurationProject? FindProject(Options o, out string? error)
        {
            try
            {
                return ConfigurationProject.Find(o.Path, o.Config, ConfigPaths.Root(), out error);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>Copies <c>%USERPROFILE%\.config\winshell</c> into the default config root once. A set override skips it.</summary>
        private static void CopyLegacyConfig()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConfigPaths.OverrideVariable)))
                return;
            try
            {
                var message = ConfigPaths.MigrateLegacy();
                if (message is not null)
                    Console.Error.WriteLine(message);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not copy the old configuration from \"{ConfigPaths.LegacyRoot()}\": {e.Message}");
            }
        }

        private static int RunShell(Options o, string[] originalArgs)
        {
            // Restarted to update: the shell before this one lets go of the taskbar, the hotkeys and the tray first.
            if (o.After is { } previous)
                Instances.WaitForExit(previous);
            // A downloaded update goes in place before any shell runs, and then the new winshell.exe runs the shell.
            // System.Diagnostics.Process is loaded first, from the files this one started with.
            RuntimeHelpers.PrepareMethod(typeof(Cli).GetMethod(nameof(RunUpdated), BindingFlags.NonPublic | BindingFlags.Static)!.MethodHandle);
            using (Process.GetCurrentProcess()) { }
            var update = InstallUpdate(o.After);
            if (update is { Error: null })
            {
                Console.Error.WriteLine($"Updated Winshell to {update.Version}.");
                return RunUpdated(originalArgs);
            }
            if (update is { Error: { } updateError })
                Console.Error.WriteLine($"Could not update Winshell to {update.Version}: {updateError}");

            var project = FindProject(o, out var error);
            if (error is not null)
            {
                // Before any instance log exists. An attached console prints it. Start and sign-in have no window.
                StartupError.Report(error, StartupError.FolderFor(o.Path, ConfigPaths.Root()));
                return 1;
            }
            if (project is null)
            {
                if (o.Path is null)
                    CopyLegacyConfig();
                // -d starts this program again. That child runs the shell, so this process must not also detach.
                if (o.Daemonize)
                    return DetachDefault(originalArgs);
                return RunDefault(o, update);
            }
            var reason = project.Prepare(o.NoBuild, ConfigurationProject.Run);
            if (reason is not null)
            {
                StartupError.Report(reason, project.Folder);
                return 1;
            }
            return StartProgram(project.Program, ProgramArguments(o), o.Daemonize);
        }

        /// <summary>Starts <c>winshell.exe</c> again without <c>-d</c>, and returns at once. The child runs the default shell.</summary>
        private static int DetachDefault(string[] originalArgs)
        {
            var exe = Environment.ProcessPath!;
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            var childArgs = originalArgs.Where(a => a is not ("-d" or "--daemonize")).ToList();
            // The child has no console. A bare child would print help and exit, so name the run command.
            if (FirstCommand([.. childArgs]) is null)
                childArgs.Insert(0, "run");
            foreach (var a in childArgs)
                psi.ArgumentList.Add(a);
            try
            {
                using var child = Process.Start(psi);
                return 0;
            }
            catch (Win32Exception e)
            {
                StartupError.Report($"Could not start \"{exe}\": {e.Message}", null);
                return 1;
            }
        }

        /// <summary>The default shell, in this process. <c>--no-duplicate</c> and <c>-v</c> are on the command line <see cref="Shell.Run{T}"/> reads.</summary>
        private static int RunDefault(Options o, InstalledUpdate? update)
        {
            var retired = ConfigurationProject.LeftoverQml(o.Path, o.Config, ConfigPaths.Root());
            using var updater = Updater.ForRelease(ReleaseRepository, Version, Log.Default);
            if (updater is not null && update is { Error: { } failure })
                updater.ReportApplyFailure(update.Version, failure);
            var options = new ShellOptions
            {
                ShellId = "winshell-default",
                Updater = updater,
                RetiredQml = retired,
            };
            Shell.Run<DefaultShellConfiguration>(options);
            return Environment.ExitCode;
        }

        /// <summary>What the configuration's program should see. Winshell's own selectors stay here.</summary>
        private static List<string> ProgramArguments(Options o)
        {
            var args = new List<string>();
            if (o.Daemonize)
                args.Add("-d");
            if (o.NoDuplicate)
                args.Add("--no-duplicate");
            if (o.Verbose)
                args.Add("--verbose");
            args.AddRange(o.Rest);
            return args;
        }

        /// <summary>Starts a configuration's program and returns its exit code. <c>-d</c> makes that program return at once.</summary>
        private static int StartProgram(string program, IReadOnlyList<string> args, bool daemonize)
        {
            var psi = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = daemonize };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            try
            {
                using var process = Process.Start(psi)!;
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Win32Exception e)
            {
                StartupError.Report($"Could not start \"{program}\": {e.Message}", System.IO.Path.GetDirectoryName(program));
                return 1;
            }
        }

        /// <summary>
        /// Puts a downloaded update in place, unless another shell runs: it would keep loading files from this folder
        /// as it needs them and mix versions, so the update stays ready until a start finds none. A named mutex keeps
        /// two starts at the same moment, before either is listed, from installing over each other: the second skips.
        /// </summary>
        private static InstalledUpdate? InstallUpdate(int? after)
        {
            using var installing = new Mutex(false, @"Local\winshell-update");
            bool mine;
            try
            {
                mine = installing.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                mine = true;
            }
            if (!mine)
                return null;
            try
            {
                return Instances.List(false).Any(i => i.Info.Pid != after) ? null : Updater.InstallStaged(ReleaseRepository, Version);
            }
            finally
            {
                installing.ReleaseMutex();
            }
        }

        /// <summary>
        /// Runs the updated winshell.exe with this one's arguments, in this console, and ends when it does. This process
        /// stays as its parent, so a terminal or the sign-in entry's console host keeps waiting on the shell.
        /// </summary>
        private static int RunUpdated(string[] args)
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            Process shell;
            try
            {
                shell = Process.Start(psi)!;
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                StartupError.Report($"Winshell is updated, but the new winshell.exe did not start: {e.Message}. Start winshell.exe again.", null);
                return 1;
            }
            using (shell)
            {
                shell.WaitForExit();
                return shell.ExitCode;
            }
        }

        /// <summary>winshell doctor: checks this PC's setup, prints a line each, and fails when a check fails. It never builds.</summary>
        private static int Doctor(Options o)
        {
            var root = ConfigPaths.Root();
            var project = FindProject(o, out var problem);
            string? search = project?.Folder;
            if (search is null && o.Path is { } path)
            {
                var full = System.IO.Path.GetFullPath(path);
                search = Directory.Exists(full) ? full : System.IO.Path.GetDirectoryName(full);
            }
            else if (search is null && o.Config is { } name)
            {
                search = System.IO.Path.Combine(root, name);
            }
            var retired = problem is null && project is null ? ConfigurationProject.LeftoverQml(o.Path, o.Config, root) : null;
            var checks = Diagnostics.Doctor.Run(new DoctorContext(
                Environment.ProcessPath!,
                root,
                project?.ProjectFile,
                problem,
                DoctorInstances(),
                ShellLocations.LocalRoot,
                ShellLocations.CrashRoot,
                retired,
                search), DoctorFacts.Pc);
            if (o.Json)
            {
                Console.WriteLine(Diagnostics.Doctor.ToJson(checks, Version));
            }
            else
            {
                var color = !o.NoColor && !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
                Console.Write(Diagnostics.Doctor.Format(checks, Version, color));
            }
            return checks.Any(c => c.Status == CheckStatus.Fail) ? 1 : 0;
        }

        /// <summary>The running instances, or none when their folder cannot be read: the doctor still runs its checks.</summary>
        private static List<DoctorInstance> DoctorInstances()
        {
            try
            {
                return Instances.List(false).Select(i => new DoctorInstance(i.Info.Id, i.Info.Pid, i.Info.ConfigPath, i.Info.Log)).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        private static InstanceChoice Select(Options o, bool includeDead = false)
        {
            var instances = Instances.List(includeDead).Select(i => i.Info).ToList();
            if (o.Pid is { } pid)
            {
                var match = instances.FirstOrDefault(i => i.Pid == pid);
                return match is null ? InstanceChoice.Miss($"No instance with process id {pid}") : InstanceChoice.Found(match);
            }
            if (o.Id is { } id)
            {
                var matches = instances.Where(i => i.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();
                return matches.Count switch
                {
                    1 => InstanceChoice.Found(matches[0]),
                    0 => InstanceChoice.Miss($"No instance with id {id}"),
                    _ => InstanceChoice.Miss($"Several instances start with {id}"),
                };
            }
            if (o.Path is not null || o.Config is not null)
            {
                var project = FindProject(o, out var error);
                if (error is not null)
                    return InstanceChoice.Miss(error);
                var match = project is null
                    ? instances.FirstOrDefault(IsDefaultShell)
                    : instances.FirstOrDefault(i => string.Equals(i.ConfigPath, project.Program, StringComparison.OrdinalIgnoreCase));
                var label = project?.Program ?? "the default shell";
                return match is null ? InstanceChoice.Miss($"No running instance of {label}") : InstanceChoice.Found(match);
            }
            return ChooseUnselected(instances);
        }

        /// <summary>
        /// The instance a command with no selector acts on. One instance is that one. Several is a command-line
        /// error: kill, log and ipc must not guess.
        /// </summary>
        internal static InstanceChoice ChooseUnselected(IReadOnlyList<InstanceInfo> instances)
        {
            if (instances.Count > 1)
            {
                var listed = string.Join(", ", instances.Select(i => $"{i.Id} ({i.ConfigPath})"));
                return InstanceChoice.Wrong($"Several instances are running; pick one with -i: {listed}");
            }
            return instances.Count == 1 ? InstanceChoice.Found(instances[0]) : InstanceChoice.Miss("No running instances");
        }

        /// <summary><c>winshell.exe</c> and <c>winshellw.exe</c> are the default shell. A project built into <c>bin\shell</c> is not.</summary>
        private static bool IsDefaultShell(InstanceInfo info)
        {
            var name = System.IO.Path.GetFileName(info.ConfigPath);
            return (name.Equals("winshell.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("winshellw.exe", StringComparison.OrdinalIgnoreCase))
                && ConfigurationProject.ForProgram(info.ConfigPath) is null;
        }

        internal readonly record struct InstanceChoice(InstanceInfo? Info, string? Error, int Code)
        {
            public static InstanceChoice Found(InstanceInfo info) => new(info, null, 0);

            public static InstanceChoice Miss(string? error) => new(null, error, 1);

            public static InstanceChoice Wrong(string error) => new(null, error, 2);
        }

        private static int List(Options o)
        {
            var instances = Instances.List(false);
            if (o.Json)
            {
                Console.WriteLine(CliJson.Instances(instances.Select(i => i.Info)));
                return 0;
            }
            if (instances.Count == 0)
            {
                Console.WriteLine("No running instances.");
                return 0;
            }
            foreach (var (info, _) in instances)
            {
                Console.WriteLine($"Instance {info.Id}:");
                Console.WriteLine($"  Process ID: {info.Pid}");
                Console.WriteLine($"  Shell ID: {info.ShellId}");
                Console.WriteLine($"  Config path: {info.ConfigPath}");
                Console.WriteLine($"  Launch time: {info.LaunchTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Log file: {info.Log}");
            }
            return 0;
        }

        private static async Task<int> Kill(Options o)
        {
            var choice = Select(o);
            if (choice.Info is not { } info)
            {
                Console.Error.WriteLine(choice.Error);
                return choice.Code;
            }
            // Null once the instance has accepted the kill. No answer is a failure too.
            string? failure = $"Instance {info.Id} closed the control pipe without answering.";
            try
            {
                await foreach (var response in IpcServer.Request(info.Pipe, new IpcRequest("kill")))
                {
                    failure = response.Ok ? null : $"Instance {info.Id} refused: {response.Error}";
                    break;
                }
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
            {
                failure = $"Could not reach instance {info.Id}: {e.Message}";
            }
            // The quit is only posted. Wait so the next start does not overlap the AppBar, hotkeys and tray.
            if (!WaitForShellExit(info, TimeSpan.FromSeconds(5)) && !(o.Force && EndProcess(info)))
            {
                if (failure is not null)
                    Console.Error.WriteLine(failure);
                Console.Error.WriteLine(o.Force
                    ? $"Could not end instance {info.Id}."
                    : $"Instance {info.Id} is still running. Use --force to end it.");
                return 1;
            }
            Console.WriteLine($"Killed {info.Id}");
            return 0;
        }

        private static async Task<int> Reload(Options o)
        {
            var choice = Select(o);
            if (choice.Info is not { } info)
            {
                Console.Error.WriteLine(choice.Error);
                return choice.Code;
            }
            var project = ConfigurationProject.ForProgram(info.ConfigPath);
            if (project is not null)
            {
                string? reason;
                try
                {
                    reason = project.Prepare(noBuild: false, ConfigurationProject.Run);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    reason = e.Message;
                }
                if (reason is not null)
                {
                    Console.Error.WriteLine(reason);
                    await ShowBuildError(info, reason, project.ProjectFile);
                    return 1;
                }
            }
            var beside = project?.RebuiltBeside;
            try
            {
                await foreach (var response in IpcServer.Request(info.Pipe, new IpcRequest(
                    "reload",
                    Args: beside is null ? null : [beside],
                    Hard: o.Hard)))
                {
                    if (response.Ok)
                    {
                        Console.WriteLine($"Reloaded {info.Id}");
                        return 0;
                    }
                    Console.Error.WriteLine($"Instance {info.Id} did not reload: {response.Error}");
                    return 1;
                }
                Console.Error.WriteLine($"Instance {info.Id} closed the control pipe without answering.");
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not reach instance {info.Id}: {e.Message}");
            }
            return 1;
        }

        /// <summary>Asks the instance to show the build error. The instance keeps running either way.</summary>
        private static async Task ShowBuildError(InstanceInfo info, string error, string projectFile)
        {
            try
            {
                await foreach (var response in IpcServer.Request(info.Pipe, new IpcRequest("build-error", Args: [error, projectFile])))
                {
                    if (!response.Ok)
                        Console.Error.WriteLine($"Instance {info.Id} could not show the notice: {response.Error}");
                    return;
                }
                Console.Error.WriteLine($"Instance {info.Id} could not show the notice.");
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Instance {info.Id} could not show the notice: {e.Message}");
            }
        }

        /// <summary>True when this instance's process has exited, or the process id now belongs to something else.</summary>
        private static bool WaitForShellExit(InstanceInfo info, TimeSpan timeout)
        {
            try
            {
                using var process = Process.GetProcessById(info.Pid);
                if (process.HasExited || !Instances.IsThisInstance(info, process))
                    return true;
                return process.WaitForExit(timeout);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return true;
            }
        }

        private static bool EndProcess(InstanceInfo info)
        {
            try
            {
                using var process = Process.GetProcessById(info.Pid);
                // A different process now owns this id. The instance we asked is already gone.
                if (process.HasExited || !Instances.IsThisInstance(info, process))
                    return true;
                process.Kill(entireProcessTree: true);
                return process.WaitForExit(5000);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return e is ArgumentException;
            }
        }

        private static async Task<int> PrintLog(Options o)
        {
            string? path = o.Positional.FirstOrDefault();
            if (path is null)
            {
                var choice = Select(o);
                if (choice.Info is not { } info)
                {
                    // Several running instances is a command-line error, not a reason to show a stopped log.
                    if (choice.Code == 2)
                    {
                        Console.Error.WriteLine(choice.Error);
                        return 2;
                    }
                    // A selector can name an instance that stopped, but never gets a different one instead.
                    var selected = o.Pid is not null || o.Id is not null || o.Path is not null || o.Config is not null;
                    var stopped = selected ? Select(o, includeDead: true).Info : Instances.List(true).FirstOrDefault().Info;
                    if (stopped is null)
                    {
                        Console.Error.WriteLine(choice.Error);
                        return choice.Code;
                    }
                    info = stopped;
                    Console.Error.WriteLine($"Showing the log of stopped instance {info.Id}.");
                }
                path = info.Log;
            }
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No log at {path}");
                return 1;
            }
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var all = (await reader.ReadToEndAsync()).TrimEnd('\n').Split('\n');
            foreach (var line in o.Tail is { } n ? all.TakeLast(n) : all)
            {
                if (line.Length > 0)
                    Console.WriteLine(line.TrimEnd('\r'));
            }
            while (o.Follow)
            {
                var line = await reader.ReadLineAsync();
                if (line is null)
                {
                    await Task.Delay(200);
                    continue;
                }
                Console.WriteLine(line);
            }
            return 0;
        }

        private static async Task<int> Ipc(Options o)
        {
            var args = o.Positional;
            if (args.Count == 0)
            {
                PrintHelp("ipc");
                return 2;
            }
            IpcRequest request = args[0] switch
            {
                "show" => new IpcRequest("show", Json: o.Json),
                "call" when args.Count >= 3 => new IpcRequest("call", args[1], args[2], args.Skip(3).ToList()),
                "prop" when args.Count >= 4 && args[1] == "get" => new IpcRequest("prop", args[2], args[3]),
                "wait" when args.Count >= 3 => new IpcRequest("wait", args[1], args[2]),
                "listen" when args.Count >= 3 => new IpcRequest("listen", args[1], args[2]),
                _ => new IpcRequest(""),
            };
            if (request.Op.Length == 0)
            {
                Console.Error.WriteLine("Usage: winshell ipc call <target> <function> [args...] | prop get <target> <property> | wait|listen <target> <signal> | show");
                return 2;
            }
            var choice = Select(o);
            if (choice.Info is not { } info)
            {
                Console.Error.WriteLine(choice.Error);
                return choice.Code;
            }
            var answered = false;
            try
            {
                await foreach (var response in IpcServer.Request(info.Pipe, request))
                {
                    answered = true;
                    if (!response.Ok)
                    {
                        Console.Error.WriteLine(response.Error);
                        return 1;
                    }
                    if (!string.IsNullOrEmpty(response.Value))
                        Console.WriteLine(response.Value.TrimEnd('\n'));
                    if (request.Op != "listen")
                        break;
                }
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not reach instance {info.Id}: {e.Message}");
                return 1;
            }
            // A listen ends without a value when the shell quits. Any other request always gets an answer.
            if (!answered && request.Op != "listen")
            {
                Console.Error.WriteLine($"Instance {info.Id} closed the control pipe without answering.");
                return 1;
            }
            return 0;
        }
    }
}
