using System.Security.Cryptography;
using System.Text;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Shell;

/// <summary>The settings a shell gives with <c>//@ pragma</c> comments at the top of its root file.</summary>
public sealed record ShellPragmas(string? ShellId, string? AppId, string? DataDir, string? StateDir, string? CacheDir, string? IconTheme, IReadOnlyDictionary<string, string> Env)
{
    public static ShellPragmas Read(string rootFile)
    {
        string? id = null, appId = null, data = null, state = null, cache = null, icons = null;
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(rootFile))
        {
            foreach (var raw in File.ReadLines(rootFile))
            {
                var line = raw.Trim();
                if (!line.StartsWith("//@", StringComparison.Ordinal))
                {
                    if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                        continue;
                    break;
                }
                var parts = line[3..].Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0] != "pragma")
                    continue;
                var value = parts.Length > 2 ? parts[2].Trim() : "";
                switch (parts[1])
                {
                    case "ShellId": id = value; break;
                    case "AppId": appId = value; break;
                    case "DataDir": data = value; break;
                    case "StateDir": state = value; break;
                    case "CacheDir": cache = value; break;
                    case "IconTheme": icons = value; break;
                    case "Env":
                        var eq = value.IndexOf('=');
                        if (eq > 0)
                            env[value[..eq].Trim()] = value[(eq + 1)..].Trim();
                        break;
                }
            }
        }
        return new ShellPragmas(id, appId, data, state, cache, icons, env);
    }
}

/// <summary>
/// Runs a shell: makes an engine for each generation, hands old objects to new ones on reload, and reloads when
/// the shell's files change.
/// </summary>
public sealed class ShellHost : IShellHost, IDisposable
{
    private readonly string _rootFile;
    private readonly IEventLoop _loop;
    private readonly IShellPlatform? _platform;
    private readonly Func<ModuleRegistry> _modules;
    private readonly Action<QmlEngine>? _configure;
    private readonly ShellSettings _settings = new();
    private readonly List<DirectoryWatch> _watchers = [];
    private ScreenRegistry? _screens;
    private QmlEngine? _engine;
    private QObject? _root;
    private IDisposable? _reloadTimer;
    // When the last load began reading its files. A save after this that no watcher saw still reloads.
    private DateTime _loadStarted;
    private bool _inhibitPopup;
    private bool _disposed;

    public ShellHost(string rootFile, IEventLoop loop, IShellPlatform? platform, Log log, Func<ModuleRegistry>? modules = null, Action<QmlEngine>? configure = null)
    {
        _rootFile = Path.GetFullPath(rootFile);
        _loop = loop;
        _platform = platform;
        Log = log;
        _modules = modules ?? StandardModules.Create;
        _configure = configure;
        ShellDir = Path.GetDirectoryName(_rootFile)!;
        var appFromEnv = Environment.GetEnvironmentVariable("WINSHELL_APP_ID");
        var pragmas = ShellPragmas.Read(_rootFile);
        Pragmas = pragmas;
        ShellId = pragmas.ShellId ?? Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(_rootFile.ToLowerInvariant())));
        AppId = string.IsNullOrEmpty(pragmas.AppId) ? (string.IsNullOrEmpty(appFromEnv) ? "winshell" : appFromEnv) : pragmas.AppId;
        LaunchTime = DateTime.Now;
        InstanceId = Base36(Environment.ProcessId) + Base36(new DateTimeOffset(LaunchTime).ToUnixTimeSeconds());
        DataDir = Expand(pragmas.DataDir, ShellLocations.DataRoot) ?? Path.Combine(ShellLocations.DataRoot, "by-shell", ShellId);
        StateDir = Expand(pragmas.StateDir, ShellLocations.StateRoot) ?? Path.Combine(ShellLocations.StateRoot, "by-shell", ShellId);
        CacheDir = Expand(pragmas.CacheDir, ShellLocations.CacheRoot) ?? Path.Combine(ShellLocations.CacheRoot, "by-shell", ShellId);
        foreach (var (k, v) in pragmas.Env)
            Environment.SetEnvironmentVariable(k, v);
    }

    private static string? Expand(string? pragma, string baseDir) =>
        pragma is null ? null : Path.GetFullPath(pragma.Replace("$BASE", baseDir, StringComparison.Ordinal));

    public Log Log { get; }

    public ShellPragmas Pragmas { get; }

    public string ShellId { get; }

    public string InstanceId { get; }

    public string AppId { get; }

    public DateTime LaunchTime { get; }

    public ShellSettings Settings => _settings;

    public string ShellDir { get; }

    /// <summary>The configuration's root file, its <c>shell.qml</c>.</summary>
    public string RootFile => _rootFile;

    public string DataDir { get; }

    public string StateDir { get; }

    public string CacheDir { get; }

    public string ConfigRoot { get; init; } = "";

    public string LogPath { get; init; } = "";

    /// <summary>Where a window that fails writes its crash report. Null writes none, as in tests.</summary>
    public CrashReports? Crashes { get; init; }

    public bool WatchFiles
    {
        get => _settings.Watching;
        set => _settings.WatchFiles = value;
    }

    /// <summary>The instance id encoding: standard base 36, then reversed.</summary>
    internal static string Base36(long number)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = new StringBuilder();
        do
        {
            result.Insert(0, digits[(int)(number % 36)]);
            number /= 36;
        } while (number > 0);
        var chars = result.ToString().ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    public QmlEngine? Engine => _engine;

    public QObject? Root => _root;

    public IpcRegistry Ipc { get; private set; } = new();

    /// <summary>Raised after each load or reload, with the error when it failed.</summary>
    public event Action<string?>? Reloaded;

    /// <summary>Registers services every generation's engine gets, such as the clipboard.</summary>
    public List<Action<QmlEngine>> Services { get; } = [];

    public bool Start()
    {
        _screens = new ScreenRegistry(_platform);
        return Load(hard: true, initial: true);
    }

    public void Reload(bool hard) => Load(hard, initial: false);

    public void InhibitReloadPopup() => _inhibitPopup = true;

    private bool Load(bool hard, bool initial)
    {
        if (_disposed)
            return false;
        // Each load starts with the popup allowed; the old generation's reloadFailed handler can inhibit it.
        _inhibitPopup = false;
        Log.ClearFirstScriptError();
        _loadStarted = DateTime.UtcNow;
        var oldEngine = _engine;
        var oldRoot = _root;
        var engine = new QmlEngine(_modules(), _loop, Log, ShellDir);
        engine.AddService<IShellHost>(this);
        _settings.Engine = engine;
        engine.AddService(_settings);
        if (_platform is not null)
            engine.AddService(_platform);
        engine.AddService(_screens ?? new ScreenRegistry(_platform));
        var ipc = new IpcRegistry();
        engine.AddService(ipc);
        if (Crashes is not null)
            engine.AddService(Crashes);
        foreach (var service in Services)
            service(engine);
        _configure?.Invoke(engine);
        QObject root;
        // A configuration can make the runtime throw anything, not only QML errors. It is reported, and the shell
        // keeps running the generation it had, or nothing, until the files change.
        try
        {
            root = engine.CreateRoot(_rootFile);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var message = Log.Describe(e, _rootFile);
            Log.Error("scene", (initial ? "Failed to load configuration: " : "Failed to reload configuration: ") + message);
            Log.Debug("scene", e.ToString());
            DisposeFailed(engine);
            WatchFilesOf(oldEngine ?? engine);
            if (oldEngine?.FindNativeSingleton<ShellGlobal>() is { } oldShell)
                oldShell.Emit("reloadFailed", message);
            Reloaded?.Invoke(message);
            return false;
        }
        var previousIpc = Ipc;
        _engine = engine;
        _root = root;
        Ipc = ipc;
        // Hand the old objects to the new ones, then end the reload stage. A throw here is a failed load: the new
        // generation is discarded and, on a reload, the generation that was running stays.
        try
        {
            if (root is Reloadable reloadable)
                reloadable.Reload(hard ? null : oldRoot);
            else
                Reloadable.ReloadChildrenRecursive(root, hard ? null : oldRoot);
            engine.FinishReload();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var message = Log.Describe(e, _rootFile);
            Log.Error("scene", (initial ? "Failed to load configuration: " : "Failed to reload configuration: ") + message);
            Log.Error("scene", e.ToString());
            _engine = oldEngine;
            _root = oldRoot;
            Ipc = previousIpc;
            _settings.Engine = oldEngine;
            DisposeFailed(engine);
            WatchFilesOf(oldEngine ?? engine);
            if (oldEngine?.FindNativeSingleton<ShellGlobal>() is { } failedShell)
                failedShell.Emit("reloadFailed", message);
            Reloaded?.Invoke(message);
            return false;
        }
        var scriptError = engine.Log.FirstScriptError;
        oldEngine?.Dispose();
        WatchFilesOf(engine);
        if (!initial)
            Log.Info("", "Configuration Loaded");
        engine.FindNativeSingleton<ShellGlobal>()?.Emit("reloadCompleted");
        Reloaded?.Invoke(scriptError);
        return true;
    }

    /// <summary>Disposes the engine of a load that failed, which may have failed halfway through making its objects.</summary>
    private void DisposeFailed(QmlEngine engine)
    {
        try
        {
            engine.Dispose();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Debug("scene", e.ToString());
        }
    }

    /// <summary>Whether the reload popup should show for the reload that just ran.</summary>
    public bool ShowReloadPopup => !_inhibitPopup && Environment.GetEnvironmentVariable("WINSHELL_NO_RELOAD_POPUP") != "1";

    private void WatchFilesOf(QmlEngine engine)
    {
        foreach (var w in _watchers)
            w.Dispose();
        _watchers.Clear();
        if (_disposed)
            return;
        var returned = false;
        foreach (var dir in WatchedDirectories(engine))
        {
            if (Directory.Exists(dir))
            {
                ArmDirectory(engine, dir);
            }
            else
            {
                ArmParent(engine, dir);
                // A folder that came back before its parent watch started raises no event.
                returned |= Directory.Exists(dir);
            }
        }
        // The old watchers are gone and the new ones only see what comes next. A file written in between, such as a
        // shell.qml still being written when a reload read it, would otherwise wait for the next save.
        if (returned || ChangedSince(engine, _loadStarted))
            _loop.Post(ScheduleReload);
    }

    private bool ChangedSince(QmlEngine engine, DateTime since) =>
        engine.LoadedFiles.Append(_rootFile).Any(file => File.GetLastWriteTimeUtc(file) >= since);

    /// <summary>Directories of the loaded QML, including ones that are not there yet.</summary>
    private List<string> WatchedDirectories(QmlEngine engine) =>
        engine.LoadedFiles.Append(_rootFile)
            .Select(f => Path.GetDirectoryName(Path.GetFullPath(f))!)
            .Where(dir => !string.IsNullOrEmpty(dir))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void ArmDirectory(QmlEngine engine, string dir)
    {
        DirectoryWatch? watch = null;
        watch = DirectoryWatch.Start(dir, DirectoryWatch.FileChanges, name =>
        {
            if (IsShellFile(name))
                _loop.Post(ScheduleReload);
            // Deleting the folder itself does not arrive as a file event. When the folder is already gone, arm its parent.
            if (!Directory.Exists(dir))
                _loop.Post(() => RecoverWatch(engine, watch!));
        }, () => _loop.Post(() => RecoverWatch(engine, watch!)));
        if (watch is null)
            ArmParent(engine, dir);
        else
            _watchers.Add(watch);
    }

    private static bool IsShellFile(string name)
    {
        var ext = Path.GetExtension(name);
        return ext.Equals(".qml", StringComparison.OrdinalIgnoreCase) || ext.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(name).Equals("qmldir", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Watches the nearest folder that still exists. A git checkout deletes the shell folder and creates it again;
    /// the old watcher is dead, and this one sees the folder return.
    /// </summary>
    private void ArmParent(QmlEngine engine, string dir)
    {
        var parent = NearestExistingParent(dir);
        if (parent is null)
            return;
        DirectoryWatch? watch = null;
        watch = DirectoryWatch.Start(parent, DirectoryWatch.NameChanges, name =>
        {
            var changed = Path.GetFullPath(Path.Combine(parent, name)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (dir.Equals(changed, StringComparison.OrdinalIgnoreCase)
                || dir.StartsWith(changed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                _loop.Post(() => RecoverWatch(engine, watch!));
        }, () => _loop.Post(() => RecoverWatch(engine, watch!)));
        if (watch is not null)
            _watchers.Add(watch);
    }

    /// <summary>
    /// Builds the watchers again after one dies. A folder that came back schedules a reload. A folder that is still
    /// missing keeps the parent watch and waits.
    /// </summary>
    private void RecoverWatch(QmlEngine engine, DirectoryWatch source)
    {
        if (_disposed || !_watchers.Contains(source))
            return;
        var missingBefore = WatchedDirectories(engine).Any(dir => !Directory.Exists(dir));
        WatchFilesOf(engine);
        var stillMissing = WatchedDirectories(engine).Any(dir => !Directory.Exists(dir));
        if (!missingBefore || !stillMissing)
            ScheduleReload();
    }

    private static string? NearestExistingParent(string dir)
    {
        var current = Path.GetDirectoryName(dir);
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current))
                return current;
            var next = Path.GetDirectoryName(current);
            if (next == current)
                return null;
            current = next;
        }
        return null;
    }

    private void ScheduleReload()
    {
        if (!WatchFiles || _disposed)
            return;
        // Editors write a file in several steps; wait for them to finish.
        _reloadTimer?.Dispose();
        _reloadTimer = _loop.Schedule(TimeSpan.FromMilliseconds(100), () => Reload(hard: false));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _reloadTimer?.Dispose();
        foreach (var w in _watchers)
            w.Dispose();
        _engine?.Dispose();
        _settings.Destroy();
        _screens?.Destroy();
        _ = Theme.Current;
    }
}
