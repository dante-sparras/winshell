using System.Security.Cryptography;
using System.Text;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Quickshell;

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
    private readonly QuickshellSettings _settings = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private ScreenRegistry? _screens;
    private QmlEngine? _engine;
    private QObject? _root;
    private IDisposable? _reloadTimer;
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
        var appFromEnv = Environment.GetEnvironmentVariable("QS_APP_ID");
        var pragmas = ShellPragmas.Read(_rootFile);
        Pragmas = pragmas;
        ShellId = pragmas.ShellId ?? Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(_rootFile.ToLowerInvariant())));
        AppId = string.IsNullOrEmpty(pragmas.AppId) ? (string.IsNullOrEmpty(appFromEnv) ? "org.quickshell" : appFromEnv) : pragmas.AppId;
        LaunchTime = DateTime.Now;
        InstanceId = Base36(Environment.ProcessId) + Base36(new DateTimeOffset(LaunchTime).ToUnixTimeSeconds());
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DataDir = Expand(pragmas.DataDir, Path.Combine(local, "winshell", "data")) ?? Path.Combine(local, "winshell", "data", "by-shell", ShellId);
        StateDir = Expand(pragmas.StateDir, Path.Combine(local, "winshell", "state")) ?? Path.Combine(local, "winshell", "state", "by-shell", ShellId);
        CacheDir = Expand(pragmas.CacheDir, Path.Combine(local, "winshell", "cache")) ?? Path.Combine(local, "winshell", "cache", "by-shell", ShellId);
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

    public QuickshellSettings Settings => _settings;

    public string ShellDir { get; }

    public string DataDir { get; }

    public string StateDir { get; }

    public string CacheDir { get; }

    public bool WatchFiles
    {
        get => _settings.Watching;
        set => _settings.WatchFiles = value;
    }

    /// <summary>Quickshell's instance id encoding: standard base 36, then reversed.</summary>
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
        foreach (var service in Services)
            service(engine);
        _configure?.Invoke(engine);
        QObject root;
        try
        {
            root = engine.CreateRoot(_rootFile);
        }
        catch (Exception e) when (e is Qml.QmlSyntaxException or QmlCompileException or QmlException or IOException or Jint.Runtime.JavaScriptException)
        {
            var message = e.Message;
            Log.Error("scene", (initial ? "Failed to load configuration: " : "Failed to reload configuration: ") + message);
            engine.Dispose();
            WatchFilesOf(oldEngine ?? engine);
            if (oldEngine?.FindNativeSingleton<QuickshellGlobal>() is { } oldQs)
                oldQs.Emit("reloadFailed", message);
            Reloaded?.Invoke(message);
            return false;
        }
        _engine = engine;
        _root = root;
        Ipc = ipc;
        _inhibitPopup = false;
        // Hand the old objects to the new ones, then end the reload stage.
        if (root is Reloadable reloadable)
            reloadable.Reload(hard ? null : oldRoot);
        else
            Reloadable.ReloadChildrenRecursive(root, hard ? null : oldRoot);
        engine.FinishReload();
        oldEngine?.Dispose();
        WatchFilesOf(engine);
        if (!initial)
            Log.Info("", "Configuration Loaded");
        engine.FindNativeSingleton<QuickshellGlobal>()?.Emit("reloadCompleted");
        Reloaded?.Invoke(null);
        return true;
    }

    /// <summary>Whether the reload popup should show for the reload that just ran.</summary>
    public bool ShowReloadPopup => !_inhibitPopup && Environment.GetEnvironmentVariable("QS_NO_RELOAD_POPUP") != "1";

    private void WatchFilesOf(QmlEngine engine)
    {
        foreach (var w in _watchers)
            w.Dispose();
        _watchers.Clear();
        var dirs = engine.LoadedFiles.Append(_rootFile)
            .Select(f => Path.GetDirectoryName(Path.GetFullPath(f))!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists);
        foreach (var dir in dirs)
        {
            var watcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            void OnChange(object? sender, FileSystemEventArgs e)
            {
                var ext = Path.GetExtension(e.FullPath);
                if (!ext.Equals(".qml", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".js", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileName(e.FullPath).Equals("qmldir", StringComparison.OrdinalIgnoreCase))
                    return;
                _loop.Post(ScheduleReload);
            }
            watcher.Changed += OnChange;
            watcher.Created += OnChange;
            watcher.Deleted += OnChange;
            watcher.Renamed += (s, e) => OnChange(s, e);
            _watchers.Add(watcher);
        }
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
