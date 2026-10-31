using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Winshell;
using Winshell.Platform.Windows;
using Winshell.Platform.Windows.Diagnostics;
using Winshell.Platform.Windows.Services.Updates;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

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

            public bool NoColor { get; set; }

            public int? Tail { get; set; }

            public List<string> Positional { get; } = [];
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
                if (token is not ("-p" or "--path" or "-c" or "--config" or "-n" or "--no-duplicate"
                    or "-d" or "--daemonize" or "-v" or "--verbose"))
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
                    case "--force":
                        o.Force = true;
                        break;
                    case "-t" or "--tail":
                        o.Tail = WholeNumber(args[i], Next());
                        break;
                    case "--no-color":
                        o.NoColor = true;
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
                          show                                  list IPC targets
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
                       winshell log [-f] [-t N] [file]     print an instance's log
                       winshell ipc show                   list IPC targets
                       winshell ipc call <target> <function> [args...]
                       winshell ipc prop get <target> <property>
                       winshell ipc wait <target> <signal>
                       winshell ipc listen <target> <signal>
                       winshell doctor [config options] [-j] [--no-color]
                                                           check this PC's setup for Winshell
                       winshell help                       print this summary

                Config selection:
                  -p, --path <path>      a shell.qml, or the folder it is in
                  -c, --config <name>    a named config in {ConfigPaths.Root()}
                                         ({ConfigPaths.OverrideVariable} overrides that folder)
                Instance selection (kill, log, ipc):
                  -i, --id <id>          an instance id, or the start of one
                      --pid <pid>        an instance's process id
                Other:
                  -d, --daemonize        run in the background
                  -n, --no-duplicate     exit quietly if this config is already running
                  -v, --verbose          log debug messages
                  -j, --json             print list or doctor as JSON
                      --no-color         print doctor without color
                  -h, --help, /?         print help and exit
                  -V, --version          print the version
                  --                     end options; later words are arguments

                Exit codes: 0 done, 1 the command failed, 2 the command line was wrong.
                """);
        }

        private static void WriteHelp(string text) => Console.WriteLine(text.Replace("\r\n", "\n").TrimEnd());

        /// <summary>The root file of the shell to run, or null with a message when there is none.</summary>
        private static string? ResolveConfig(Options o, out string? error)
        {
            if (o.Path is null)
                CopyLegacyConfig();
            var found = FindConfig(o, out error);
            // No config yet: run the Windows 11 shell Winshell ships with.
            return found ?? (error is null ? DefaultShell.Install() : null);
        }

        /// <summary>
        /// The config <see cref="ResolveConfig"/> picks, without copying an old config or writing the default shell:
        /// null with no error when Winshell would run its default shell, and null with the reason when it cannot pick.
        /// </summary>
        private static string? FindConfig(Options o, out string? error)
        {
            error = null;
            if (o.Path is { } path)
            {
                var full = System.IO.Path.GetFullPath(path);
                if (File.Exists(full))
                    return full;
                var inDir = System.IO.Path.Combine(full, "shell.qml");
                if (File.Exists(inDir))
                    return inDir;
                error = $"No shell.qml at {full}";
                return null;
            }
            var baseDir = ConfigPaths.Root();
            if (o.Config is { } name)
            {
                var named = System.IO.Path.Combine(baseDir, name, "shell.qml");
                if (File.Exists(named))
                    return named;
                error = $"No config named \"{name}\" in {baseDir}";
                return null;
            }
            var root = System.IO.Path.Combine(baseDir, "shell.qml");
            if (File.Exists(root))
                return root;
            var def = System.IO.Path.Combine(baseDir, "default", "shell.qml");
            if (File.Exists(def))
                return def;
            if (Directory.Exists(baseDir))
            {
                var configs = Directory.EnumerateDirectories(baseDir).Select(d => System.IO.Path.Combine(d, "shell.qml")).Where(File.Exists).ToList();
                if (configs.Count == 1)
                    return configs[0];
                if (configs.Count > 1)
                {
                    error = $"Several configs in {baseDir}; pick one with -c: {string.Join(", ", configs.Select(c => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(c))))}";
                    return null;
                }
            }
            return null;
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
                WaitForExit(previous);
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

            var root = ResolveConfig(o, out var error);
            if (root is null)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
            // The parent of -d returns at once. It must not hold the mutex, or the child looks like a duplicate of it.
            if (o.Daemonize)
            {
                var exe = Environment.ProcessPath!;
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
                var childArgs = originalArgs.Where(a => a is not ("-d" or "--daemonize")).ToList();
                // The child has no console. A bare child would print help and exit, so name the run command.
                if (FirstCommand([.. childArgs]) is null)
                    childArgs.Insert(0, "run");
                foreach (var a in childArgs)
                    psi.ArgumentList.Add(a);
                using var child = Process.Start(psi);
                return 0;
            }
            // Held until this method returns, which is the life of the shell. A second --no-duplicate start then exits.
            using var duplicate = o.NoDuplicate ? DuplicateHold.TryClaim(root) : default;
            if (o.NoDuplicate && !duplicate.Acquired)
                return 0;

            Instances.Clean();
            var id = Instances.NewId();
            var dir = Instances.DirOf(id);
            Directory.CreateDirectory(dir);
            var logPath = System.IO.Path.Combine(dir, "log.txt");
            var instanceLog = new InstanceLog(logPath);
            // Objects with no engine log through Log.Default. That is this process's log.
            var log = Log.Default;
            log.MinimumLevel = o.Verbose ? LogLevel.Debug : LogLevel.Info;
            var consoleLock = new object();
            var logClosed = false;
            log.Sink = (level, category, message) =>
            {
                var label = level switch
                {
                    LogLevel.Debug => "DEBUG",
                    LogLevel.Info => " INFO",
                    LogLevel.Warn => " WARN",
                    LogLevel.Error => "ERROR",
                    _ => "FATAL",
                };
                var line = string.IsNullOrEmpty(category) ? $"{label}: {message}" : $"{label} {category}: {message}";
                lock (consoleLock)
                {
                    // A crash can still be reported after the log file is closed; then it goes to the console only.
                    // The file outlives the run and goes into crash reports, so secrets are taken out of it.
                    try
                    {
                        if (!logClosed)
                            instanceLog.Write(level, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {CrashRedaction.Redact(line)}");
                    }
                    catch (Exception e) when (e is IOException or ObjectDisposedException)
                    {
                    }
                    Console.Error.WriteLine(line);
                }
            };

            // A crash is reported in a crash report that outlives the log, and in the instance's log, where
            // winshell log finds it, before the process ends. Failures the shell survives get a report each too.
            var crashes = new CrashReports(CrashFolder()) { ConfigPath = root, LogPath = logPath, Log = log };
            var exitState = Path.Combine(ShellLocations.StateRoot, "last-exit.toml");
            void Reported(CrashReport? report)
            {
                if (report is not null)
                    Quietly(() => log.Info("", $"Wrote a crash report to \"{report.Path}\""));
            }
            Exception? fatal = null;
            // Writes the report first, since logging can fail as the process ends. Returns false, so it can be an
            // exception filter: that runs before the finally blocks below close the log.
            bool ReportFatal(Exception error)
            {
                if (ReferenceEquals(fatal, error))
                    return false;
                fatal = error;
                var report = crashes.Write("Winshell", error, fatal: true);
                Quietly(() => log.Write(LogLevel.Fatal, "", $"Winshell crashed: {error}"));
                Reported(report);
                return false;
            }
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception error)
                    ReportFatal(error);
                else
                    Quietly(() => log.Write(LogLevel.Fatal, "", $"Winshell crashed: {e.ExceptionObject}"));
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Quietly(() => log.Error("", $"A background task failed: {e.Exception}"));
                e.SetObserved();
                Reported(crashes.WriteOnce("background task", e.Exception, recovery: "A background task failed. Winshell is still running."));
            };

            using var loop = new WinEventLoop { Log = log };
            loop.Failed += e => Reported(crashes.WriteOnce("event loop", e, subject: "Winshell", recovery: "Something in Winshell failed. It is still running."));
            using var updater = Updater.ForRelease(ReleaseRepository, Version, log);
            if (updater is not null)
            {
                if (update is { Error: { } failure })
                    updater.ReportApplyFailure(update.Version, failure);
                // Starts this winshell.exe again, with the same arguments, which puts the update in place as it starts.
                updater.Restart = () =>
                {
                    // That start would not install while another shell runs, so restarting would change nothing.
                    if (Instances.List(false).Any(i => i.Info.Pid != Environment.ProcessId))
                        return "Another Winshell is running. Close it, then restart this one.";
                    loop.Post(Restart);
                    return null;
                };
                void Restart()
                {
                    var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var a in originalArgs.Where(a => a is not ("-d" or "--daemonize")))
                        psi.ArgumentList.Add(a);
                    psi.ArgumentList.Add("--after");
                    psi.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                    log.Info("updates", "Restarting Winshell to update it.");
                    using var next = Process.Start(psi);
                    loop.Quit(0);
                }
            }
            using var platform = new WindowsPlatform(loop, log);
            WindowsModules.Install(platform);
            var host = new ShellHost(root, loop, platform, log) { ConfigRoot = ConfigPaths.Root(), LogPath = logPath, Crashes = crashes };
            crashes.ShellId = host.ShellId;
            // Winshell's own notice for a config error or a window that failed; it shows even when the config does not.
            using var notices = new ShellNotices(host, loop, platform, log, path => ShellNotices.Open(path, log));
            host.Services.Add(engine =>
            {
                engine.AddService<IClipboard>(platform);
                engine.AddService<IPlatformServices>(platform);
                engine.AddService<IIconLookup>(IconProvider.Instance);
                WindowsModules.AddServices(engine, platform);
                WindowsModules.AddStartup(engine, Environment.ProcessPath!, originalArgs);
                if (updater is not null)
                    WindowsModules.AddUpdates(engine, updater);
                // Winshell Settings' About page runs the same checks as winshell doctor, for this shell's config.
                engine.AddService<IDoctor>(new CliDoctor(() => DoctorContextFor(root, null)));
            });
            log.Info("shell", $"Winshell {ProductVersion.Current.Text} on Windows {Environment.OSVersion.Version}, config \"{root}\"");
            log.Info("", $"Launching config: \"{root}\"");
            log.Info("", $"Shell ID: \"{host.ShellId}\" Path ID: \"{host.ShellId}\"");
            log.Info("", $"Saving logs to \"{logPath}\"");
            var pipe = $"winshell-{id}";
            NamedPipeServerStream listening;
            try
            {
                listening = IpcServer.OpenAvailable(ref pipe);
            }
            catch (IOException)
            {
                log.Error("", "The control pipe is already taken.");
                lock (consoleLock)
                {
                    logClosed = true;
                    instanceLog.Dispose();
                }
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
                {
                }
                return 1;
            }
            if (!string.Equals(pipe, $"winshell-{id}", StringComparison.Ordinal))
                log.Warn("", $"The control pipe winshell-{id} was taken. Listening on {pipe}.");
            using var ipc = new IpcServer(listening, pipe, loop, () => host, () => loop.Quit(0), dismissNotice: () => notices.Hide());
            var processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
            Instances.Write(new InstanceInfo(id, Environment.ProcessId, host.ShellId, root, DateTime.Now, pipe, logPath, processStart));
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                loop.Post(() => loop.Quit(0));
            };
            int code;
            try
            {
                if (!host.Start() && host.Root is null)
                    log.Warn("", "The config failed to load. Winshell keeps running and reloads it when the files change.");
                if (host.Root is not null)
                    Quietly(() =>
                    {
                        if (CrashFollowUp.Find(crashes.Folder, exitState) is not { } closed)
                            return;
                        CrashFollowUp.MarkShown(exitState, closed.Path);
                        var recovery = closed.ExceptionLine.Length > 0 ? closed.ExceptionLine : "Winshell closed the last time it ran.";
                        var previous = new CrashReport(closed.Path, "Winshell", new InvalidOperationException(recovery), true)
                        {
                            Subject = "Winshell",
                            Recovery = recovery,
                        };
                        loop.Post(() => notices.Show(ShellNotices.ForCrash(previous)));
                    });
#if DEBUG
                if (Environment.GetEnvironmentVariable(TestCrashVariable) is { Length: > 0 })
                    loop.Schedule(TimeSpan.FromMilliseconds(300), () => loop.Quit(0));
#endif
                code = loop.Run();
#if DEBUG
                if (Environment.GetEnvironmentVariable(TestCrashVariable) is { Length: > 0 })
                    throw new InvalidOperationException($"{TestCrashVariable} asked Winshell to crash after its config loaded.");
#endif
            }
            catch (Exception e) when (ReportFatal(e))
            {
                throw;
            }
            finally
            {
                if (fatal is null)
                    Quietly(() => CrashFollowUp.MarkClean(exitState, DateTimeOffset.Now));
                host.Dispose();
                Instances.Remove(id);
                lock (consoleLock)
                {
                    logClosed = true;
                    instanceLog.Dispose();
                }
            }
            return code;
        }

        /// <summary>
        /// Read only by the crash report test, and only in a Debug build: crash reports go to this folder, and the
        /// shell crashes once its config has loaded and its loop has run, so the test sees the report a real crash writes.
        /// A Release build ignores the variable.
        /// </summary>
        internal const string TestCrashVariable = "WINSHELL_TEST_CRASH";

        private static string CrashFolder()
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable(TestCrashVariable) is { Length: > 0 } folder)
                return folder;
#endif
            return ShellLocations.CrashRoot;
        }

        /// <summary>Runs what reports a crash; that must not throw a second exception over the first.</summary>
        private static void Quietly(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
            }
        }

        /// <summary>
        /// Session mutex for one configuration. <c>--no-duplicate</c> holds it for the life of the shell, so two starts
        /// that both pass the instance list, because neither has written it yet, cannot both keep running. The name is
        /// a hash so a long path still fits, and the hash ignores case so Windows paths match.
        /// </summary>
        internal static string DuplicateMutexName(string configPath)
        {
            var full = System.IO.Path.GetFullPath(configPath).ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)));
            return @"Local\winshell-" + hash;
        }

        /// <summary>Owns the <c>--no-duplicate</c> mutex until disposed. A failed claim owns nothing, and that start exits.</summary>
        private readonly struct DuplicateHold : IDisposable
        {
            private readonly Mutex? _mutex;

            private DuplicateHold(Mutex mutex) => _mutex = mutex;

            public bool Acquired => _mutex is not null;

            public static DuplicateHold TryClaim(string configPath)
            {
                var mutex = new Mutex(false, DuplicateMutexName(configPath));
                bool mine;
                try
                {
                    mine = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    // The previous shell crashed while it held this. The wait succeeded and this start owns it.
                    mine = true;
                }
                if (!mine)
                {
                    mutex.Dispose();
                    return default;
                }
                // A shell started without --no-duplicate holds no mutex. Its instance.json is the only record of it.
                var running = Instances.List(false).FirstOrDefault(i => string.Equals(i.Info.ConfigPath, configPath, StringComparison.OrdinalIgnoreCase));
                if (running.Info is not null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                    return default;
                }
                return new DuplicateHold(mutex);
            }

            public void Dispose()
            {
                if (_mutex is null)
                    return;
                _mutex.ReleaseMutex();
                _mutex.Dispose();
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

        private static void WaitForExit(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.WaitForExit(TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException)
            {
                // It has already gone.
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
                Console.Error.WriteLine($"Winshell is updated, but the new winshell.exe did not start: {e.Message}. Start winshell.exe again.");
                return 1;
            }
            using (shell)
            {
                shell.WaitForExit();
                return shell.ExitCode;
            }
        }

        /// <summary>winshell doctor: checks this PC's setup, prints a line each, and fails when a check fails.</summary>
        private static int Doctor(Options o)
        {
            string? file;
            string? problem;
            try
            {
                file = FindConfig(o, out problem);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The config folder cannot be listed: that is the Config check's failure, and the others still run.
                file = null;
                problem = $"Winshell cannot read the config folder: {e.Message}";
            }
            var checks = Platform.Windows.Diagnostics.Doctor.Run(DoctorContextFor(file, problem));
            if (o.Json)
            {
                Console.WriteLine(Platform.Windows.Diagnostics.Doctor.ToJson(checks, Version));
            }
            else
            {
                var color = !o.NoColor && !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
                Console.Write(Platform.Windows.Diagnostics.Doctor.Format(checks, Version, color));
            }
            return checks.Any(c => c.Status == CheckStatus.Fail) ? 1 : 0;
        }

        private static DoctorContext DoctorContextFor(string? configFile, string? problem) => new(
            Environment.ProcessPath!,
            ConfigPaths.Root(),
            configFile,
            problem,
            DoctorInstances(),
            ShellLocations.LocalRoot,
            ShellLocations.CrashRoot);

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

        private sealed class CliDoctor(Func<DoctorContext> context) : IDoctor
        {
            public IReadOnlyList<DoctorCheck> Run() => Platform.Windows.Diagnostics.Doctor.Run(context());
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
                var root = ResolveConfig(o, out var error);
                if (root is null)
                    return InstanceChoice.Miss(error);
                var match = instances.FirstOrDefault(i => string.Equals(i.ConfigPath, root, StringComparison.OrdinalIgnoreCase));
                return match is null ? InstanceChoice.Miss($"No running instance of {root}") : InstanceChoice.Found(match);
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
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(instances.Select(i => i.Info).ToList(), InstanceJson.Default.ListInstanceInfo));
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

        /// <summary>True when this instance's process has exited, or the process id now belongs to something else.</summary>
        private static bool WaitForShellExit(InstanceInfo info, TimeSpan timeout)
        {
            try
            {
                using var process = Process.GetProcessById(info.Pid);
                if (process.HasExited || !IsThisInstance(info, process))
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
                if (process.HasExited || !IsThisInstance(info, process))
                    return true;
                process.Kill(entireProcessTree: true);
                return process.WaitForExit(5000);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return e is ArgumentException;
            }
        }

        private static bool IsThisInstance(InstanceInfo info, Process process)
        {
            if (!process.ProcessName.StartsWith("winshell", StringComparison.OrdinalIgnoreCase))
                return false;
            if (info.ProcessStart == 0)
                return true;
            try
            {
                var started = process.StartTime.ToUniversalTime().Ticks;
                return started == 0 || started == info.ProcessStart;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                return true;
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
                "show" => new IpcRequest("show"),
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
