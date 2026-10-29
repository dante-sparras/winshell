using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Winshell;
using Winshell.Platform.Windows;
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

            public bool Daemonize { get; set; }

            public bool NoDuplicate { get; set; }

            public bool Verbose { get; set; }

            public bool Json { get; set; }

            public bool All { get; set; }

            public bool Follow { get; set; }

            public int? Tail { get; set; }

            public List<string> Positional { get; } = [];
        }

        private static readonly string Version = typeof(Cli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        public static async Task<int> Run(string[] args)
        {
            var command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null;
            var rest = command is null ? args : args[1..];
            Options options;
            try
            {
                options = Parse(rest);
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
                    if (args.Contains("-h") || args.Contains("--help"))
                    {
                        PrintHelp();
                        return 0;
                    }
                    return RunShell(options, args);
                case "list":
                    return List(options);
                case "kill":
                    return await Kill(options);
                case "log":
                    return await PrintLog(options);
                case "ipc":
                    return await Ipc(options);
                case "msg":
                    Console.Error.WriteLine("winshell msg is deprecated; use winshell ipc call.");
                    options.Positional.Insert(0, "call");
                    return await Ipc(options);
                case "help":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown command \"{command}\". Run winshell --help.");
                    return 2;
            }
        }

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
                        o.Pid = int.Parse(Next(), CultureInfo.InvariantCulture);
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
                    case "-t" or "--tail":
                        o.Tail = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "-V" or "--version" or "-h" or "--help" or "--no-color" or "--log-times" or "--any-display" or "--newest":
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

        private static void PrintHelp()
        {
            Console.WriteLine($"""
                winshell {Version}: a desktop shell for Windows 11

                Usage: winshell [options]                  run a shell
                       winshell list [-a] [-j]             list running instances
                       winshell kill [instance options]    stop an instance
                       winshell log [-f] [-t N]            print an instance's log
                       winshell ipc show                   list IPC targets
                       winshell ipc call <target> <function> [args...]
                       winshell ipc prop get <target> <property>
                       winshell ipc wait <target> <signal>
                       winshell ipc listen <target> <signal>

                Config selection:
                  -p, --path <path>      a shell.qml, or the folder it is in
                  -c, --config <name>    a named config in {ConfigPaths.Root()}
                                         ({ConfigPaths.OverrideVariable} overrides that folder)
                Instance selection:
                  -i, --id <id>          an instance id, or the start of one
                      --pid <pid>        an instance's process id
                Other:
                  -d, --daemonize        run in the background
                  -n, --no-duplicate     do nothing if the shell is already running
                  -v, --verbose          log debug messages
                  -V, --version          print the version
                """);
        }

        /// <summary>The root file of the shell to run, or null with a message when there is none.</summary>
        private static string? ResolveConfig(Options o, out string? error)
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
            CopyLegacyConfig();
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
            // No config yet: run the Windows 11 shell Winshell ships with.
            return DefaultShell.Install();
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
                foreach (var a in originalArgs.Where(a => a is not ("-d" or "--daemonize")))
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
            var log = new Log { MinimumLevel = o.Verbose ? LogLevel.Debug : LogLevel.Info };
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
            var crashes = new CrashReports(CrashFolder()) { ConfigPath = root, LogPath = logPath };
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
                Reported(crashes.WriteOnce("background task", e.Exception));
            };

            using var loop = new WinEventLoop { Log = log };
            loop.Failed += e => Reported(crashes.WriteOnce("event loop", e));
            using var platform = new WindowsPlatform(loop, log);
            WindowsModules.Install(platform);
            var host = new ShellHost(root, loop, platform, log) { ConfigRoot = ConfigPaths.Root(), LogPath = logPath, Crashes = crashes };
            crashes.ShellId = host.ShellId;
            host.Services.Add(engine =>
            {
                engine.AddService<IClipboard>(platform);
                engine.AddService<IPlatformServices>(platform);
                engine.AddService<IIconLookup>(IconProvider.Instance);
                WindowsModules.AddServices(engine, platform);
                WindowsModules.AddStartup(engine, Environment.ProcessPath!, originalArgs);
            });
            log.Info("", $"Launching config: \"{root}\"");
            log.Info("", $"Shell ID: \"{host.ShellId}\" Path ID: \"{host.ShellId}\"");
            log.Info("", $"Saving logs to \"{logPath}\"");
            var pipe = $"winshell-{id}";
            var processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
            Instances.Write(new InstanceInfo(id, Environment.ProcessId, host.ShellId, root, DateTime.Now, pipe, logPath, processStart));
            using var ipc = new IpcServer(pipe, loop, () => host, () => loop.Quit(0));
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
            await foreach (var _ in IpcServer.Request(info.Pipe, new IpcRequest("kill")))
                break;
            Console.WriteLine($"Killed {info.Id}");
            return 0;
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
            var all = (await reader.ReadToEndAsync()).Split('\n');
            foreach (var line in o.Tail is { } n ? all.TakeLast(n + 1) : all)
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
            try
            {
                await foreach (var response in IpcServer.Request(info.Pipe, request))
                {
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
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                Console.Error.WriteLine($"Could not reach instance {info.Id}: {e.Message}");
                return 1;
            }
            return 0;
        }
    }
}
