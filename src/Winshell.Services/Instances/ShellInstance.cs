using System.Diagnostics;
using System.IO.Pipes;

namespace Winshell;

/// <summary>
/// What makes a process a Winshell instance, in <c>winshell.exe</c> and in a configuration's own program alike: its
/// folder and log, its crash reports, its control pipe, and the record <c>winshell list</c> finds it by.
/// <see cref="InstanceStart"/> calls <see cref="Open"/>, <see cref="OpenPipe"/>, <see cref="Listen"/> and
/// <see cref="Publish"/> in that order. A start whose pipe failed ends with <see cref="Abandon"/>, and anything else
/// with <see cref="Close"/>.
/// </summary>
internal sealed class ShellInstance
{
    private readonly InstanceLog _file;
    private readonly Instances _instances;
    private readonly string _dir;
    private readonly string _shellId;
    private readonly string _configPath;
    private NamedPipeServerStream? _listening;
    private IpcServer? _server;
    private Exception? _fatal;

    private ShellInstance(ShellLocations locations, string id, string shellId, string configPath, Log log)
    {
        Id = id;
        _instances = locations.Instances;
        _dir = _instances.DirOf(id);
        Directory.CreateDirectory(_dir);
        _shellId = shellId;
        _configPath = configPath;
        Log = log;
        LogPath = Path.Combine(_dir, InstanceLog.FileName);
        Pipe = $"winshell-{id}";
        _file = new InstanceLog(LogPath);
        Crashes = new CrashReports(locations.CrashRoot, log, shellId, configPath, LogPath);
    }

    public string Id { get; }

    public string LogPath { get; }

    public Log Log { get; }

    public CrashReports Crashes { get; }

    /// <summary>The control pipe's name. It differs from the id's only when that name was taken.</summary>
    public string Pipe { get; private set; }

    /// <summary>The record of the last clean exit, which <see cref="CrashReports"/> reads.</summary>
    public static string ExitState => Path.Combine(ShellLocations.Current.StateRoot, "last-exit.toml");

    /// <summary>Runs first when a crash on another thread is ending the process, which skips every <c>finally</c>.</summary>
    public Action? Crashing { get; set; }

    /// <summary>
    /// Makes this process's folder and log, and reports crashes from here on. <paramref name="log"/> writes to that
    /// log from now on, and keeps Debug lines when <paramref name="verbose"/>. <paramref name="configPath"/> is what
    /// <c>winshell list</c> shows as the configuration.
    /// </summary>
    public static ShellInstance Open(string shellId, string configPath, Log log, bool verbose = false)
    {
        var locations = ShellLocations.Current;
        locations.Instances.Clean();
        var instance = new ShellInstance(locations, Instances.NewId(), shellId, configPath, log);
        log.WriteTo(instance.WriteLine, verbose ? LogLevel.Debug : LogLevel.Info);
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
            instance.Reported(instance.Crashes.WriteOnce("background task", e.Exception,
                recovery: "A background task failed. Winshell is still running."));
        };
        return instance;
    }

    /// <summary>
    /// Opens the control pipe. Null when it opened, or else why not, which is also logged. A start that failed goes on
    /// to <see cref="Abandon"/>.
    /// </summary>
    public string? OpenPipe()
    {
        var pipe = Pipe;
        try
        {
            _listening = IpcServer.OpenAvailable(ref pipe);
        }
        catch (IOException e)
        {
            Log.Error("shell", e.Message);
            return e.Message;
        }

        if (!string.Equals(pipe, Pipe, StringComparison.Ordinal))
            Log.Warn("shell", $"The control pipe {Pipe} was taken. Listening on {pipe}.");
        Pipe = pipe;
        return null;
    }

    /// <summary>Starts serving the pipe <see cref="OpenPipe"/> opened, on <paramref name="loop"/>.</summary>
    public void Listen(IEventLoop loop, IInstanceControl control)
    {
        _server = new IpcServer(_listening!, Pipe, loop, Log, control);
        _listening = null;
    }

    /// <summary>Writes the record, after which <c>winshell list</c> shows this process.</summary>
    public void Publish()
    {
        // What the startup logged lands first, so a listed shell always has a log to show.
        _file.Flush();
        using var process = Process.GetCurrentProcess();
        _instances.Write(new InstanceInfo(
            Id,
            Environment.ProcessId,
            _shellId,
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
        var report = Crashes.Write("Winshell", error, true);
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
            Quietly(() => CrashReports.MarkClean(ExitState, TimeProvider.System.GetLocalNow()));
    }

    /// <summary>Stops the pipe, marks the record exited and closes the log. The folder stays for <c>winshell log</c>.</summary>
    public void Close()
    {
        _server?.Dispose();
        _listening?.Dispose();
        _listening = null;
        _instances.Remove(Id);
        _file.Dispose();
    }

    /// <summary>Takes back a start that never published, so nothing of it stays behind.</summary>
    public void Abandon()
    {
        _listening?.Dispose();
        _listening = null;
        _file.Dispose();
        try
        {
            Directory.Delete(_dir, true);
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

    /// <summary>
    /// Adds one line to the file. The file outlives the run and crash reports copy its end, so this is where secrets
    /// are taken out. Its time is the process's real time. Once the file is closed, a line goes to the console only.
    /// </summary>
    private void WriteLine(LogLevel level, string line)
    {
        try
        {
            _file.Write(level,
                $"{TimeProvider.System.GetLocalNow():yyyy-MM-dd HH:mm:ss.fff} {CrashRedaction.Redact(line)}");
        }
        catch (IOException)
        {
            // The disk refused the line. That must not fail the code that logged it, and the console still has it.
        }
    }
}
