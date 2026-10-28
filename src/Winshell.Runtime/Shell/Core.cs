using System.Diagnostics;
using Jint;
using Jint.Native;

namespace Winshell.Runtime.Shell;

/// <summary>The <c>ShellScreen</c>: one monitor. The object stays the same while the monitor is connected.</summary>
public sealed class ShellScreen : QObject
{
    private ScreenInfo _info;

    public ShellScreen(ScreenInfo info)
    {
        _info = info;
    }

    public string Id => _info.Id;

    public ScreenInfo Info => _info;

    internal void Update(ScreenInfo info)
    {
        var old = _info;
        _info = info;
        if (old.Name != info.Name) NotifyChanged("name");
        if (old.Model != info.Model) NotifyChanged("model");
        if (old.Geometry != info.Geometry)
        {
            NotifyChanged("x");
            NotifyChanged("y");
            NotifyChanged("width");
            NotifyChanged("height");
            NotifyChanged("orientation");
            NotifyChanged("primaryOrientation");
        }
        if (old.Geometry != info.Geometry || old.DevicePixelRatio != info.DevicePixelRatio || old.PhysicalWidthMm != info.PhysicalWidthMm)
        {
            NotifyChanged("devicePixelRatio");
            NotifyChanged("logicalPixelDensity");
            NotifyChanged("physicalPixelDensity");
        }
        if (old.WorkArea != info.WorkArea) NotifyChanged("workArea");
        if (old.RefreshRate != info.RefreshRate) NotifyChanged("refreshRate");
    }

    [Qml] public string Name { get { Track(); return _info.Name; } }

    [Qml] public string Model { get { Track(); return _info.Model; } }

    [Qml] public string Manufacturer { get { Track(); return _info.Manufacturer; } }

    [Qml] public string SerialNumber { get { Track(); return _info.SerialNumber; } }

    [Qml] public double X { get { Track(); return _info.Geometry.X; } }

    [Qml] public double Y { get { Track(); return _info.Geometry.Y; } }

    [Qml] public double Width { get { Track(); return _info.Geometry.Width; } }

    [Qml] public double Height { get { Track(); return _info.Geometry.Height; } }

    /// <summary>
    /// A Windows extension: the part of the screen windows maximize into, without the taskbar and the zones other
    /// app bars reserve, in the same logical coordinates as <c>x</c> and <c>y</c>.
    /// </summary>
    [Qml] public QRect WorkArea { get { Track(); return _info.WorkArea; } }

    [Qml] public double DevicePixelRatio { get { Track(); return _info.DevicePixelRatio; } }

    [Qml]
    public double PhysicalPixelDensity
    {
        get
        {
            Track();
            return _info.PhysicalWidthMm > 0 ? _info.Geometry.Width * _info.DevicePixelRatio / _info.PhysicalWidthMm : 96 / 25.4 * _info.DevicePixelRatio;
        }
    }

    [Qml]
    public double LogicalPixelDensity
    {
        get
        {
            Track();
            return _info.PhysicalWidthMm > 0 ? _info.Geometry.Width / _info.PhysicalWidthMm : 96 / 25.4;
        }
    }

    [Qml] public int Orientation { get { Track(); return _info.Geometry.Width >= _info.Geometry.Height ? 2 : 1; } }

    [Qml] public int PrimaryOrientation => Orientation;

    [Qml] public int RefreshRate { get { Track(); return _info.RefreshRate; } }

    public override string ToString() => $"ShellScreen({_info.Name})";

    [Qml("toString")]
    public string Describe() => ToString();
}

/// <summary>Keeps one ShellScreen per connected monitor and the list of them up to date.</summary>
public sealed class ScreenRegistry : QObject
{
    private readonly IShellPlatform? _platform;
    private readonly List<ShellScreen> _screens = [];

    public ScreenRegistry(IShellPlatform? platform)
    {
        _platform = platform;
        if (platform is not null)
        {
            platform.ScreensChanged += Refresh;
            Refresh();
        }
    }

    public IReadOnlyList<ShellScreen> Screens
    {
        get
        {
            TrackQml("screens");
            return _screens;
        }
    }

    public ShellScreen? Find(string id) => _screens.FirstOrDefault(s => s.Id == id);

    public ShellScreen? Primary => _screens.FirstOrDefault(s => s.Info.IsPrimary) ?? _screens.FirstOrDefault();

    public void Refresh()
    {
        if (_platform is null || IsDestroyed)
            return;
        var infos = _platform.Screens;
        var changed = false;
        foreach (var screen in _screens.ToArray())
        {
            if (infos.All(i => i.Id != screen.Id))
            {
                _screens.Remove(screen);
                changed = true;
            }
        }
        foreach (var info in infos)
        {
            var existing = _screens.FirstOrDefault(s => s.Id == info.Id);
            if (existing is null)
            {
                _screens.Add(new ShellScreen(info) { Engine = Engine });
                changed = true;
            }
            else
            {
                existing.Update(info);
            }
        }
        var ordered = infos.Select(i => _screens.First(s => s.Id == i.Id)).ToList();
        if (!ordered.SequenceEqual(_screens))
        {
            _screens.Clear();
            _screens.AddRange(ordered);
            changed = true;
        }
        if (changed)
            NotifyChanged("screens");
    }

    protected override void OnDestroy()
    {
        if (_platform is not null)
            _platform.ScreensChanged -= Refresh;
    }
}

/// <summary>What the running shell offers its QML: reloading, its folders and its id.</summary>
public interface IShellHost
{
    string ShellId { get; }

    /// <summary>This process's id: reversed base 36 of the pid, then of the launch time.</summary>
    string InstanceId { get; }

    /// <summary>The desktop application id. A <c>//@ pragma AppId</c> or <c>WINSHELL_APP_ID</c> overrides <c>winshell</c>.</summary>
    string AppId { get; }

    DateTime LaunchTime { get; }

    string ShellDir { get; }

    string DataDir { get; }

    string StateDir { get; }

    string CacheDir { get; }

    bool WatchFiles { get; set; }

    void Reload(bool hard);

    void InhibitReloadPopup();
}

/// <summary>The <c>Shell</c> singleton: the screens, the clipboard, reloads and processes of this shell.</summary>
[QmlSignal("lastWindowClosed")]
[QmlSignal("reloadCompleted")]
[QmlSignal("reloadFailed", "errorString")]
public sealed class ShellGlobal : QObject
{
    private readonly ScreenRegistry _screens;

    private readonly ShellSettings _settings;
    private readonly IDisposable _lastWindow;
    private readonly Quick.IClipboard? _clipboard;
    private readonly Forward _screensForward;

    public ShellGlobal(QmlEngine engine)
    {
        Engine = engine;
        _settings = engine.GetService<ShellSettings>() ?? new ShellSettings { Engine = engine };
        if (engine.GetService<ShellSettings>() is null)
            engine.AddService(_settings);
        // The settings and screens outlive this generation, so what this object hangs on them goes with it.
        _settings.Mirrored += NotifyChanged;
        _lastWindow = _settings.Connect("lastWindowClosed", _ => Emit("lastWindowClosed"));
        _clipboard = engine.GetService<Quick.IClipboard>();
        if (_clipboard is not null)
            _clipboard.Changed += OnClipboardChanged;
        _screens = engine.GetService<ScreenRegistry>() ?? new ScreenRegistry(null);
        _screensForward = new Forward(this);
        _screens.AddObserver("screens", _screensForward);
    }

    private void OnClipboardChanged()
    {
        if (!IsDestroyed)
            NotifyChanged("clipboardText");
    }

    protected override void OnDestroy()
    {
        _settings.Mirrored -= NotifyChanged;
        _screens.RemoveObserver("screens", _screensForward);
        _lastWindow.Dispose();
        if (_clipboard is not null)
            _clipboard.Changed -= OnClipboardChanged;
    }

    private IShellHost? Host => Engine?.GetService<IShellHost>();

    private sealed class Forward(QObject owner) : IPropertyObserver
    {
        public void OnPropertyChanged(QObject sender, string property) => owner.NotifyChanged("screens");
    }

    [Qml]
    public IReadOnlyList<ShellScreen> Screens
    {
        get
        {
            Track();
            using (DependencyTracker.Suspend())
                return _screens.Screens.ToList();
        }
    }

    [Qml] public string ShellDir => Host?.ShellDir ?? Engine?.ShellDir ?? "";

    [Qml] public string ShellRoot => ShellDir;

    [Qml] public string ConfigDir => ShellDir;

    [Qml] public string DataDir => Ensure(Host?.DataDir);

    [Qml] public string StateDir => Ensure(Host?.StateDir);

    [Qml] public string CacheDir => Ensure(Host?.CacheDir);

    private static string Ensure(string? dir)
    {
        if (string.IsNullOrEmpty(dir))
            return "";
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Qml] public int ProcessId => Environment.ProcessId;

    [Qml] public string InstanceId { get { Track(); return Host?.InstanceId ?? ""; } }

    [Qml] public string ShellId { get { Track(); return Host?.ShellId ?? ""; } }

    [Qml] public string AppId { get { Track(); return Host?.AppId ?? "winshell"; } }

    [Qml] public DateTime LaunchTime { get { Track(); return Host?.LaunchTime ?? default; } }

    [Qml]
    public string WorkingDirectory
    {
        get
        {
            Track();
            using (DependencyTracker.Suspend())
                return _settings.WorkingDirectory;
        }
        set => _settings.WorkingDirectory = value;
    }

    [Qml]
    public bool WatchFiles
    {
        get
        {
            Track();
            using (DependencyTracker.Suspend())
                return _settings.WatchFiles;
        }
        set => _settings.WatchFiles = value;
    }

    [Qml]
    public string ClipboardText
    {
        get
        {
            Track();
            return Engine?.GetService<Quick.IClipboard>()?.GetText() ?? "";
        }
        set
        {
            Engine?.GetService<Quick.IClipboard>()?.SetText(value ?? "");
            NotifyChanged("clipboardText");
        }
    }

    [Qml]
    public object? Env(string variable) => Environment.GetEnvironmentVariable(variable);

    [Qml]
    public void ExecDetached(object? context)
    {
        var spec = ProcessSpec.From(context);
        if (spec.Command.Count == 0)
        {
            Engine?.Log.Warn("scene", "Shell.execDetached: no command given");
            return;
        }
        ProcessLauncher.StartDetached(spec, Engine?.Log ?? Log.Default);
    }

    [Qml]
    public string IconPath(string icon, object? checkOrFallback = null)
    {
        var provider = Engine?.GetService<IIconLookup>();
        var unwrapped = ValueConverter.Unwrap(checkOrFallback);
        if (unwrapped is bool check && check && provider is not null && !provider.HasIcon(icon))
            return "";
        if (unwrapped is string fallback && fallback.Length > 0 && provider is not null && !provider.HasIcon(icon))
            return "image://icon/" + Uri.EscapeDataString(fallback);
        return "image://icon/" + Uri.EscapeDataString(icon);
    }

    [Qml]
    public bool HasThemeIcon(string icon) => Engine?.GetService<IIconLookup>()?.HasIcon(icon) ?? false;

    [Qml] public string ShellPath(string path) => Path.Combine(ShellDir, path);

    [Qml] public string ConfigPath(string path) => Path.Combine(ShellDir, path);

    [Qml] public string DataPath(string path) => Path.Combine(DataDir, path);

    [Qml] public string StatePath(string path) => Path.Combine(StateDir, path);

    [Qml] public string CachePath(string path) => Path.Combine(CacheDir, path);

    [Qml]
    public void Reload(bool hard = false) => Engine?.Loop.Post(() => Host?.Reload(hard));

    [Qml]
    public void InhibitReloadPopup() => Host?.InhibitReloadPopup();

    /// <summary>Winshell's API is version 0.3, with no unreleased features: the Quickshell version it started from.</summary>
    [Qml]
    public bool HasVersion(int major, int minor, object? features = null) =>
        (major, minor) switch
        {
            (0, <= 3) => ValueConverter.ToList(features).Count == 0 || minor < 3,
            ( < 0, _) => true,
            _ => false,
        };

    [Qml]
    public bool HasQtVersion(int major, int minor) => major < 6 || (major == 6 && minor <= 10);
}

/// <summary>Looks up named icons, for <c>Shell.iconPath</c>.</summary>
public interface IIconLookup
{
    bool HasIcon(string name);
}

/// <summary>A command to run, with its environment and working directory.</summary>
public sealed record ProcessSpec(IReadOnlyList<string> Command, IReadOnlyDictionary<string, string?> Environment, bool ClearEnvironment, string? WorkingDirectory)
{
    public static ProcessSpec From(object? context)
    {
        var value = context is JsValue js ? js : null;
        if (value is not null && value.IsObject() && !value.IsArray())
        {
            var obj = value.AsObject();
            var command = ValueConverter.ToList(ValueConverter.FromJs(obj.Get("command"))).Select(v => ValueConverter.ToQmlString(v)).ToList();
            var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var envValue = obj.Get("environment");
            if (envValue.IsObject())
            {
                var e = envValue.AsObject();
                foreach (var key in e.GetOwnPropertyKeys())
                {
                    var v = e.Get(key);
                    env[key.ToString()] = v.IsNull() || v.IsUndefined() ? null : v.ToString();
                }
            }
            var clear = obj.Get("clearEnvironment") is var c && c.IsBoolean() && c.AsBoolean();
            var wd = obj.Get("workingDirectory") is var w && w.IsString() ? w.AsString() : null;
            return new ProcessSpec(command, env, clear, wd);
        }
        var list = ValueConverter.ToList(context).Select(v => ValueConverter.ToQmlString(v)).ToList();
        return new ProcessSpec(list, new Dictionary<string, string?>(), false, null);
    }
}

/// <summary>Starts processes the way Windows users expect: through PATH, PATHEXT, and the right host for scripts.</summary>
public static class ProcessLauncher
{
    public static ProcessStartInfo CreateStartInfo(ProcessSpec spec, bool redirect)
    {
        var (file, args) = Resolve(spec.Command);
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            RedirectStandardInput = redirect,
            WorkingDirectory = spec.WorkingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        if (spec.ClearEnvironment)
            psi.Environment.Clear();
        foreach (var (k, v) in spec.Environment)
        {
            if (v is null)
                psi.Environment.Remove(k);
            else
                psi.Environment[k] = v;
        }
        if (redirect)
        {
            psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
            psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
        }
        return psi;
    }

    public static void StartDetached(ProcessSpec spec, Log log)
    {
        try
        {
            using var p = Process.Start(CreateStartInfo(spec, false));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log.Warn("process", $"Failed to start {string.Join(' ', spec.Command)}: {e.Message}");
        }
    }

    /// <summary>The program to start and its arguments. Batch files run through cmd and PowerShell scripts through pwsh.</summary>
    public static (string File, List<string> Args) Resolve(IReadOnlyList<string> command)
    {
        var program = command[0];
        var args = command.Skip(1).ToList();
        var path = Which(program) ?? program;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".cmd" or ".bat":
                return (System.Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", ["/d", "/c", path, .. args]);
            case ".ps1":
                return (Which("pwsh") ?? Which("powershell") ?? "powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path, .. args]);
            case ".sh":
                return (Which("bash") ?? Which("sh") ?? "bash", [path, .. args]);
        }
        return (path, args);
    }

    /// <summary>Finds a program on PATH, trying each PATHEXT extension.</summary>
    public static string? Which(string program)
    {
        if (Path.IsPathRooted(program) || program.Contains('/') || program.Contains('\\'))
            return File.Exists(program) ? Path.GetFullPath(program) : null;
        var exts = (System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), program);
                if (Path.HasExtension(program) && File.Exists(candidate))
                    return candidate;
                foreach (var ext in exts.Concat([".ps1"]))
                {
                    if (File.Exists(candidate + ext))
                        return candidate + ext;
                }
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }
}

/// <summary>The <c>SystemClock</c>.</summary>
public sealed class SystemClock : QObject
{
    private bool _enabled = true;
    private int _precision = (int)Precision.Seconds;
    private DateTime _date = DateTime.Now;
    private IDisposable? _timer;

    [QmlEnum]
    public enum Precision
    {
        Hours = 1,
        Minutes = 2,
        Seconds = 3,
    }

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set
        {
            if (SetField(ref _enabled, value))
                Schedule();
        }
    }

    [Qml("precision")]
    public int PrecisionValue
    {
        get { Track("precision"); return _precision; }
        set
        {
            if (SetField(ref _precision, value, "precision"))
                Tick();
        }
    }

    [Qml]
    public DateTime Date
    {
        get { Track(); return _date; }
    }

    [Qml] public int Hours { get { Track(); return _date.Hour; } }

    [Qml] public int Minutes { get { Track(); return _precision >= 2 ? _date.Minute : 0; } }

    [Qml] public int Seconds { get { Track(); return _precision >= 3 ? _date.Second : 0; } }

    public override void ComponentComplete() => Tick();

    private DateTime Truncate(DateTime now) => _precision switch
    {
        1 => new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind),
        2 => new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind),
        _ => new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind),
    };

    private void Tick()
    {
        if (IsDestroyed)
            return;
        var now = Truncate(DateTime.Now);
        if (now != _date)
        {
            var old = _date;
            _date = now;
            NotifyChanged("date");
            if (old.Hour != now.Hour) NotifyChanged("hours");
            if (old.Minute != now.Minute) NotifyChanged("minutes");
            if (old.Second != now.Second) NotifyChanged("seconds");
        }
        Schedule();
    }

    private void Schedule()
    {
        _timer?.Dispose();
        _timer = null;
        if (!_enabled || Engine is null || IsDestroyed)
            return;
        var now = DateTime.Now;
        var next = _precision switch
        {
            1 => Truncate(now).AddHours(1),
            2 => Truncate(now).AddMinutes(1),
            _ => Truncate(now).AddSeconds(1),
        };
        // Wake just after the boundary, so the new second is already here.
        _timer = Engine.Loop.Schedule(next - now + TimeSpan.FromMilliseconds(5), Tick);
    }

    protected override void OnDestroy() => _timer?.Dispose();
}

/// <summary>The <c>ElapsedTimer</c>.</summary>
public sealed class ElapsedTimer : QObject
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    [Qml] public double Elapsed() => _watch.Elapsed.TotalSeconds;

    [Qml] public double ElapsedMs() => _watch.Elapsed.TotalMilliseconds;

    [Qml] public double ElapsedNs() => _watch.Elapsed.Ticks * 100.0;

    [Qml]
    public double Restart()
    {
        var s = _watch.Elapsed.TotalSeconds;
        _watch.Restart();
        return s;
    }

    [Qml]
    public double RestartMs()
    {
        var s = _watch.Elapsed.TotalMilliseconds;
        _watch.Restart();
        return s;
    }

    [Qml]
    public double RestartNs()
    {
        var s = _watch.Elapsed.Ticks * 100.0;
        _watch.Restart();
        return s;
    }
}
