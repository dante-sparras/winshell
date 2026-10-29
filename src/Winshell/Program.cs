using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using Winshell;
using Winshell.Platform.Windows;
using Winshell.Platform.Windows.Diagnostics;
using Winshell.Platform.Windows.Services.Updates;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

return await Cli.Run(args);

namespace Winshell
{
    /// <summary>The command line.</summary>
    internal static class Cli
    {
        private sealed class Options
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

            public bool All { get; set; }

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

        public static async Task<int> Run(string[] args)
        {
            args = WithoutAfter(args, out var after);
            // Help is checked before any command runs. `winshell kill --help` must not kill the shell.
            if (args.Any(static a => a is "-h" or "--help" or "/?"))
            {
                PrintHelp(FirstCommand(args));
                return 0;
            }
            var command = FirstCommand(args);
            var rest = command is null ? args : WithoutToken(args, command);
            Options options;
            try
            {
                options = Parse(rest);
                options.After = after;
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                return 2;
            }
            switch (command)
            {
                case null:
                    if (args.Contains("-V") || args.Contains("--version"))
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

        /// <summary>The first word that is not an option. Options may come before it, so <c>-c work kill</c> is kill.</summary>
        private static string? FirstCommand(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var token = args[i];
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
            "-p" or "--path" or "-c" or "--config" or "-i" or "--id" or "--pid" or "-t" or "--tail"
            or "--log-rules" or "-m" or "--manifest" or "--debug";

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

        private static Options Parse(string[] args)
        {
            var o = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i])
                {
                    case "-p" or "--path":
                        o.Path = Next();
                        break;
                    case "-c" or "--config":
                        o.Config = Next();
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
                    case "-a" or "--all":
                        o.All = true;
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
                    case "-V" or "--version" or "-h" or "--help" or "--log-times" or "--any-display" or "--newest":
                        break;
                    case "--log-rules" or "-m" or "--manifest" or "--debug":
                        Next();
                        break;
                    default:
                        o.Positional.Add(args[i]);
                        break;
                }
            }
            return o;
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
        /// Takes <c>--after &lt;pid&gt;</c> out of the arguments. Only an update's restart passes it, and it is not one
        /// of the arguments the shell was started with: a restart or a sign-in entry must not repeat it.
        /// </summary>
        internal static string[] WithoutAfter(string[] args, out int? after)
        {
            after = null;
            var i = Array.IndexOf(args, "--after");
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
                          With no selector, the newest running instance is used.
                        """);
                    return;
                case "log":
                    WriteHelp("""
                        winshell log [-i <id> | --pid <pid> | -c <name> | -p <path>] [-t <n>] [-f] [file]
                          Prints an instance's log. -t, --tail <n> prints the last n lines.
                          -f, --follow keeps printing new lines. winshell log <file> prints that file.
                          With nothing running and no selector, the log of a stopped instance is printed.
                        """);
                    return;
                case "ipc":
                    WriteHelp("""
                        winshell ipc [-i <id> | --pid <pid> | -c <name> | -p <path>] <command>
                          show                                  list IPC targets
                          call <target> <function> [args...]    call a function and print the result
                          prop get <target> <property>          print a property
                          wait <target> <signal>                wait for one signal
                          listen <target> <signal>              print each signal until Ctrl+C
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
                  -n, --no-duplicate     do nothing if the shell is already running
                  -v, --verbose          log debug messages
                  -j, --json             print list or doctor as JSON
                      --no-color         print doctor without color
                  -h, --help, /?         print help and exit
                  -V, --version          print the version

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
            if (o.NoDuplicate)
            {
                var running = Instances.List(false).FirstOrDefault(i => string.Equals(i.Info.ConfigPath, root, StringComparison.OrdinalIgnoreCase));
                if (running.Info is not null)
                {
                    Console.Error.WriteLine($"Winshell is already running this config (instance {running.Info.Id}).");
                    return 0;
                }
            }
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

            Instances.Clean();
            var id = Instances.NewId();
            var dir = Instances.DirOf(id);
            Directory.CreateDirectory(dir);
            var logPath = System.IO.Path.Combine(dir, "log.txt");
            var logFile = new StreamWriter(logPath, append: true) { AutoFlush = true };
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
                    try
                    {
                        if (!logClosed)
                            logFile.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
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
                logFile.Dispose();
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
            using var ipc = new IpcServer(listening, pipe, loop, () => host, () => loop.Quit(0));
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
                if (Environment.GetEnvironmentVariable(TestCrashVariable) is { Length: > 0 })
                    loop.Schedule(TimeSpan.FromMilliseconds(300), () => loop.Quit(0));
                code = loop.Run();
                if (Environment.GetEnvironmentVariable(TestCrashVariable) is { Length: > 0 })
                    throw new InvalidOperationException($"{TestCrashVariable} asked Winshell to crash after its config loaded.");
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
                    logFile.Dispose();
                }
            }
            return code;
        }

        /// <summary>
        /// Read only by the crash report test: crash reports go to this folder, and the shell crashes once its config
        /// has loaded and its loop has run, so the test sees the report a real crash writes.
        /// </summary>
        internal const string TestCrashVariable = "WINSHELL_TEST_CRASH";

        private static string CrashFolder() =>
            Environment.GetEnvironmentVariable(TestCrashVariable) is { Length: > 0 } folder ? folder : ShellLocations.CrashRoot;

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

        private static (InstanceInfo? Info, string? Error) Select(Options o)
        {
            var instances = Instances.List(false);
            if (o.Pid is { } pid)
                return (instances.FirstOrDefault(i => i.Info.Pid == pid).Info, $"No instance with process id {pid}");
            if (o.Id is { } id)
            {
                var matches = instances.Where(i => i.Info.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();
                return matches.Count switch
                {
                    1 => (matches[0].Info, null),
                    0 => (null, $"No instance with id {id}"),
                    _ => (null, $"Several instances start with {id}"),
                };
            }
            if (o.Path is not null || o.Config is not null)
            {
                var root = ResolveConfig(o, out var error);
                if (root is null)
                    return (null, error);
                var match = instances.FirstOrDefault(i => string.Equals(i.Info.ConfigPath, root, StringComparison.OrdinalIgnoreCase)).Info;
                return (match, $"No running instance of {root}");
            }
            return (instances.FirstOrDefault().Info, "No running instances");
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
            var (info, error) = Select(o);
            if (info is null)
            {
                Console.Error.WriteLine(error);
                return 1;
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
                var (info, error) = Select(o);
                if (info is null)
                {
                    var dead = Instances.List(true).FirstOrDefault().Info;
                    if (dead is null)
                    {
                        Console.Error.WriteLine(error);
                        return 1;
                    }
                    info = dead;
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
                Console.Error.WriteLine("winshell ipc needs a command: show, call, prop get, wait or listen.");
                return 2;
            }
            var (info, error) = Select(o);
            if (info is null)
            {
                Console.Error.WriteLine(error);
                return 1;
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
