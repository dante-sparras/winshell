using System.Diagnostics;
using System.IO.Pipes;

namespace Winshell;

/// <summary>
/// What makes a process a Winshell instance, in <c>winshell.exe</c> and in a configuration's own program alike: its
/// folder and log, its crash reports, its control pipe, and the record <c>winshell list</c> finds it by. A start
/// calls <see cref="Open"/>, <see cref="TryOpenPipe"/>, <see cref="Listen"/> and <see cref="Publish"/> in that order.
/// A start whose pipe failed ends with <see cref="Abandon"/>, and anything else with <see cref="Close"/>.
/// </summary>
internal sealed class ShellInstance
{
    /// <summary>
    /// Read only by the crash report tests, and only in a Debug build: crash reports go to this folder, and the shell
    /// crashes once it has started, so a test sees the report a real crash writes. A Release build ignores the variable.
    /// </summary>
    internal const string TestCrashVariable = "WINSHELL_TEST_CRASH";

    private readonly object _logGate = new();
    private readonly InstanceLog _file;
    private readonly string _dir;
    private readonly string _configPath;
    private NamedPipeServerStream? _listening;
    private IpcServer? _server;
    private bool _logClosed;
    private Exception? _fatal;

    private ShellInstance(string id, string dir, string configPath, Log log)
    {
        Id = id;
        _dir = dir;
        _configPath = configPath;
        Log = log;
        LogPath = Path.Combine(dir, "log.txt");
        Pipe = $"winshell-{id}";
        _file = new InstanceLog(LogPath);
        Crashes = new CrashReports(CrashFolder()) { ConfigPath = configPath, LogPath = LogPath, Log = log };
    }

    public string Id { get; }

    public string LogPath { get; }

    public Log Log { get; }

    public CrashReports Crashes { get; }

    /// <summary>The control pipe's name. It differs from the id's only when that name was taken.</summary>
    public string Pipe { get; private set; }

    /// <summary>The record of the last clean exit, which <see cref="CrashFollowUp"/> reads.</summary>
    public static string ExitState => Path.Combine(ShellLocations.StateRoot, "last-exit.toml");

    /// <summary>Runs first when a crash on another thread is ending the process, which skips every <c>finally</c>.</summary>
    public Action? Crashing { get; set; }

    /// <summary>
    /// Makes this process's folder and log, and reports crashes from here on. <paramref name="log"/> writes to that
    /// log from now on. <paramref name="configPath"/> is what <c>winshell list</c> shows as the configuration.
    /// </summary>
    public static ShellInstance Open(string configPath, Log log, bool verbose = false)
    {
        Instances.Clean();
        var id = Instances.NewId();
        var dir = Instances.DirOf(id);
        Directory.CreateDirectory(dir);
        var instance = new ShellInstance(id, dir, configPath, log);
        // Objects with no engine log through Log.Default. That is this process's log.
        log.MinimumLevel = verbose ? LogLevel.Debug : LogLevel.Info;
        log.Sink = instance.WriteLog;
        // A crash is reported in a crash report that outlives the log, and in the instance's log, where
        // winshell log finds it, before the process ends. Failures the shell survives get a report each too.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Quietly(() => instance.Crashing?.Invoke());
            if (e.ExceptionObject is Exception error)
                instance.ReportFatal(error);
            else
                Quietly(() => log.Write(LogLevel.Fatal, "", $"Winshell crashed: {e.ExceptionObject}"));
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Quietly(() => log.Error("shell", $"A background task failed: {e.Exception}"));
            e.SetObserved();
            instance.Reported(instance.Crashes.WriteOnce("background task", e.Exception, recovery: "A background task failed. Winshell is still running."));
        };
        return instance;
    }

    /// <summary>Opens the control pipe, and logs why when it cannot. A <c>false</c> start goes on to <see cref="Abandon"/>.</summary>
    public bool TryOpenPipe()
    {
        var pipe = Pipe;
        try
        {
            _listening = IpcServer.OpenAvailable(ref pipe);
        }
        catch (IOException)
        {
            Log.Error("shell", "The control pipe is already taken.");
            return false;
        }
        if (!string.Equals(pipe, Pipe, StringComparison.Ordinal))
            Log.Warn("shell", $"The control pipe {Pipe} was taken. Listening on {pipe}.");
        Pipe = pipe;
        return true;
    }

    /// <summary>Starts serving the pipe <see cref="TryOpenPipe"/> opened, on <paramref name="loop"/>.</summary>
    public void Listen(IEventLoop loop, IInstanceControl control, Action? dismissNotice = null)
    {
        _server = new IpcServer(_listening!, Pipe, loop, control, dismissNotice: dismissNotice);
        _listening = null;
    }

    /// <summary>Writes the record, after which <c>winshell list</c> shows this process.</summary>
    public void Publish(string shellId)
    {
        using var process = Process.GetCurrentProcess();
        Instances.Write(new InstanceInfo(
            Id,
            Environment.ProcessId,
            shellId,
            _configPath,
            TimeProvider.System.GetLocalNow().LocalDateTime,
            Pipe,
            LogPath,
            process.StartTime.ToUniversalTime().Ticks,
            ProcessName: process.ProcessName));
    }

    /// <summary>
    /// Writes one fatal report first, since logging can fail as the process ends. Returns false, so it can be an
    /// exception filter: that runs before the <c>finally</c> blocks that close the log.
    /// </summary>
    public bool ReportFatal(Exception error)
    {
        if (ReferenceEquals(_fatal, error))
            return false;
        _fatal = error;
        var report = Crashes.Write("Winshell", error, fatal: true);
        Quietly(() => Log.Write(LogLevel.Fatal, "", $"Winshell crashed: {error}"));
        Reported(report);
        return false;
    }

    /// <summary>Says in the log where a report went.</summary>
    public void Reported(CrashReport? report)
    {
        if (report is not null)
            Quietly(() => Log.Info("shell", $"Wrote a crash report to \"{report.Path}\""));
    }

    /// <summary>Records a clean exit, unless this run crashed. The next start looks at it.</summary>
    public void MarkCleanExit()
    {
        if (_fatal is null)
            Quietly(() => CrashFollowUp.MarkClean(ExitState, TimeProvider.System.GetLocalNow()));
    }

    /// <summary>Stops the pipe, marks the record exited and closes the log. The folder stays for <c>winshell log</c>.</summary>
    public void Close()
    {
        _server?.Dispose();
        _listening?.Dispose();
        _listening = null;
        Instances.Remove(Id);
        CloseLog();
    }

    /// <summary>Takes back a start that never published, so nothing of it stays behind.</summary>
    public void Abandon()
    {
        _listening?.Dispose();
        _listening = null;
        CloseLog();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Runs what reports a crash; that must not throw a second exception over the first.</summary>
    internal static void Quietly(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
        }
    }

    private void WriteLog(LogLevel level, string category, string message)
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
        lock (_logGate)
        {
            // A crash can still be reported after the log file is closed; then it goes to the console only.
            // The file outlives the run and goes into crash reports, so secrets are taken out of it.
            try
            {
                if (!_logClosed)
                    _file.Write(level, $"{TimeProvider.System.GetLocalNow():yyyy-MM-dd HH:mm:ss.fff} {CrashRedaction.Redact(line)}");
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }
            Console.Error.WriteLine(line);
        }
    }

    private void CloseLog()
    {
        lock (_logGate)
        {
            _logClosed = true;
            _file.Dispose();
        }
    }

    private static string CrashFolder()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable(TestCrashVariable) is { Length: > 0 } folder)
            return folder;
#endif
        return ShellLocations.CrashRoot;
    }
}
