using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
        if (old.Geometry != info.Geometry || old.DevicePixelRatio != info.DevicePixelRatio)
            NotifyChanged("devicePixelRatio");
        if (old.WorkArea != info.WorkArea) NotifyChanged("workArea");
        if (old.RefreshRate != info.RefreshRate) NotifyChanged("refreshRate");
        var widthChanged = old.Geometry.Width != info.Geometry.Width;
        var sizeChanged = old.PhysicalWidthMm != info.PhysicalWidthMm;
        if (widthChanged || sizeChanged || old.DevicePixelRatio != info.DevicePixelRatio)
            NotifyChanged("physicalPixelDensity");
        if (widthChanged || sizeChanged)
            NotifyChanged("logicalPixelDensity");
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

    /// <summary>Physical pixels per millimeter across the monitor's width, or 0 when Windows reports no size.</summary>
    [Qml]
    public double PhysicalPixelDensity
    {
        get { Track(); return PixelsPerMillimeter(_info.Geometry.Width * _info.DevicePixelRatio, _info.PhysicalWidthMm); }
    }

    /// <summary>Device-independent pixels per millimeter: <see cref="Width"/> over the same physical width, or 0 when that width is missing.</summary>
    [Qml]
    public double LogicalPixelDensity
    {
        get { Track(); return PixelsPerMillimeter(_info.Geometry.Width, _info.PhysicalWidthMm); }
    }

    private static double PixelsPerMillimeter(double pixels, double millimeters) => millimeters > 0 ? pixels / millimeters : 0;

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

    /// <summary>The folder configurations are loaded from: <c>%APPDATA%\winshell</c>, or what <c>WINSHELL_CONFIG_DIR</c> names.
    /// Empty unless the host sets it, as <c>winshell.exe</c> does, so a test never reads or writes the user's folder.</summary>
    string ConfigRoot => "";

    /// <summary>This instance's log file, or empty when nothing writes one.</summary>
    string LogPath => "";

    bool WatchFiles { get; set; }

    void Reload(bool hard);

    void InhibitReloadPopup();

    /// <summary>
    /// A watched config file failed to parse, or parsed again. <paramref name="error"/> is empty when the file is fine.
    /// The text is <c>path[line:column]: message</c> so a notice can open the file at the line.
    /// </summary>
    void ReportFileError(FileView source, string? error) { }
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

    /// <summary>The folder configurations are loaded from, where a shell keeps the user's own choices.</summary>
    [Qml] public string ConfigRoot => Host?.ConfigRoot ?? "";

    /// <summary>This instance's log file, which <c>winshell log</c> prints. Empty when nothing writes one.</summary>
    [Qml] public string LogPath => Host?.LogPath ?? "";

    /// <summary>The Winshell release, as <c>winshell --version</c> prints it.</summary>
    [Qml] public string Version => ProductVersion.Current.Text;

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

    /// <summary>
    /// True when this Winshell release is at least <paramref name="major"/>.<paramref name="minor"/>
    /// and every named unreleased feature is present. A newer release includes those features.
    /// None are declared, so a feature list matches only on a newer release. The release is the
    /// assembly informational version, stamped from the release-please manifest.
    /// </summary>
    [Qml]
    public bool HasVersion(int major, int minor, object? features = null) =>
        ProductVersion.Current.Satisfies(major, minor, ValueConverter.ToList(features).Select(ValueConverter.ToQmlString).ToList());

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

/// <summary>
/// Starts processes through PATH and PATHEXT. A captured <c>Process</c> uses <c>CreateProcess</c>.
/// A detached start uses <c>ShellExecuteEx</c>, so elevation, App Paths and documents work, unless the
/// caller asked for an environment <c>ShellExecuteEx</c> cannot apply.
/// </summary>
public static class ProcessLauncher
{
    public static ProcessStartInfo CreateStartInfo(ProcessSpec spec, bool redirect)
    {
        var psi = Resolve(spec.Command);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = redirect;
        psi.RedirectStandardError = redirect;
        psi.RedirectStandardInput = redirect;
        psi.WorkingDirectory = spec.WorkingDirectory ?? Environment.CurrentDirectory;
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
        if (spec.Command.Count == 0)
            return;
        try
        {
            var plan = PlanDetached(spec);
            if (plan.UseShellExecute)
            {
                // ShellExecuteEx without SEE_MASK_NO_CONSOLE gives a console program its own console.
                using var started = Process.Start(new ProcessStartInfo
                {
                    FileName = plan.File,
                    Arguments = plan.Arguments,
                    UseShellExecute = true,
                    WorkingDirectory = plan.WorkingDirectory,
                });
                return;
            }
            var psi = CreateStartInfo(spec, redirect: false);
            psi.CreateNoWindow = false;
            psi.WorkingDirectory = plan.WorkingDirectory;
            if (plan.NewConsole)
                StartInNewConsole(psi);
            else
                Process.Start(psi)?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or ArgumentException)
        {
            log.Warn("process", $"Failed to start {spec.Command[0]}: {e.Message}");
        }
    }

    /// <summary>How a detached command will start. Tests read this and do not launch the program.</summary>
    internal static DetachedLaunch PlanDetached(ProcessSpec spec)
    {
        var directory = spec.WorkingDirectory ?? Profile();
        // An explicit environment cannot ride along with ShellExecuteEx, so that case stays CreateProcess.
        var customEnvironment = spec.ClearEnvironment || spec.Environment.Count > 0;
        // ShellExecuteEx opens a .sh with whatever Windows has for it. CreateProcess needs an interpreter.
        var resolved = Resolve(spec.Command, windowsOpens: !customEnvironment);
        if (!customEnvironment)
            return new DetachedLaunch(true, false, resolved.FileName, Arguments(resolved), directory);
        return new DetachedLaunch(false, IsConsoleImage(resolved.FileName), resolved.FileName, "", directory);
    }

    /// <summary>The whole command line <c>CreateProcess</c> gets.</summary>
    internal static string CommandLine(ProcessStartInfo psi)
    {
        var arguments = Arguments(psi);
        return arguments.Length == 0 ? Quote(psi.FileName) : $"{Quote(psi.FileName)} {arguments}";
    }

    /// <summary>A batch file's line is already built for cmd, and quoting it again would break it.</summary>
    private static string Arguments(ProcessStartInfo psi) =>
        psi.ArgumentList.Count == 0 ? psi.Arguments : Quote(psi.ArgumentList);

    /// <summary>The program to start and its arguments. Batch files run through cmd and PowerShell scripts through pwsh.</summary>
    public static ProcessStartInfo Resolve(IReadOnlyList<string> command, bool windowsOpens = false)
    {
        var program = command[0];
        var args = command.Skip(1).ToList();
        var path = Which(program) ?? program;
        // Win32 removes trailing dots and spaces when opening a file, including a batch file handed to cmd.
        var ext = Path.GetExtension(path.TrimEnd('.', ' ')).ToLowerInvariant();
        switch (ext)
        {
            case ".cmd" or ".bat":
                return new ProcessStartInfo(System.Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", BatchCommandLine(path, args));
            case ".ps1":
                return new ProcessStartInfo(Which("pwsh") ?? Which("powershell") ?? "powershell.exe", ["-NoProfile", "-File", path, .. args]);
            case ".sh" when !windowsOpens:
                return new ProcessStartInfo(Which("bash") ?? Which("sh") ?? "bash", [path, .. args]);
        }
        return new ProcessStartInfo(path, args);
    }

    /// <summary>
    /// cmd's arguments for a batch file. cmd parses its command line again, so the script path and any argument
    /// with a character outside a known-safe set go in quotes, where <c>&amp;|&lt;&gt;()</c> are plain text.
    /// <c>"</c>, <c>%</c>, <c>!</c>, <c>^</c> and line breaks can end the quotes or still mean something inside them,
    /// so those are refused.
    /// </summary>
    private static string BatchCommandLine(string path, List<string> args)
    {
        var line = new System.Text.StringBuilder("/d /s /c \"");
        line.Append('"').Append(BatchSafe(path)).Append('"');
        foreach (var arg in args)
        {
            var quote = arg.Length == 0 || arg.Any(c => char.IsControl(c) || char.IsAscii(c) && !char.IsAsciiLetterOrDigit(c) && !@"#$*+-./:?@\_".Contains(c));
            line.Append(' ').Append(quote ? $"\"{BatchSafe(arg)}\"" : BatchSafe(arg));
        }
        return line.Append('"').ToString();
    }

    private static string BatchSafe(string text) =>
        text.AsSpan().IndexOfAny("\"%!^\r\n\0") >= 0
            ? throw new ArgumentException($"cmd cannot pass \"{text}\" to a batch file safely, as it contains \", %, !, ^ or a line break.")
            : text;

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

    private static void StartInNewConsole(ProcessStartInfo psi)
    {
        var command = new StringBuilder(CommandLine(psi), 32768);
        var environment = EnvironmentBlock(psi);
        try
        {
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            // A partial name in lpApplicationName is not searched on PATH. Null lets the command line do that.
            var application = Path.IsPathRooted(psi.FileName) ? psi.FileName : null;
            // CREATE_NEW_CONSOLE so a console tool does not inherit the hidden sign-in console.
            if (!CreateProcess(application, command, IntPtr.Zero, IntPtr.Zero, false, 0x00000010 | 0x00000400, environment, psi.WorkingDirectory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
        }
        finally
        {
            Marshal.FreeHGlobal(environment);
        }
    }

    private static IntPtr EnvironmentBlock(ProcessStartInfo psi)
    {
        // CreateProcess wants the block in order, and a Unicode block ends with an extra empty string.
        var entries = new List<string>(psi.Environment.Count);
        foreach (var entry in psi.Environment)
            entries.Add($"{entry.Key}={entry.Value}");
        entries.Sort(StringComparer.OrdinalIgnoreCase);
        var block = new StringBuilder();
        foreach (var entry in entries)
            block.Append(entry).Append('\0');
        block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }

    private static string Profile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length > 0 ? profile : Environment.CurrentDirectory;
    }

    /// <summary>True for a Windows console image. Documents and missing files are not.</summary>
    private static bool IsConsoleImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
                return false;
            stream.Position = 0x3C;
            var pe = reader.ReadInt32();
            if (pe < 0 || pe > stream.Length - 96)
                return false;
            stream.Position = pe;
            if (reader.ReadUInt32() != 0x4550)
                return false;
            stream.Position = pe + 24 + 68;
            return reader.ReadUInt16() == 3;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException)
        {
            return false;
        }
    }

    private static string Quote(IReadOnlyList<string> args)
    {
        var text = new StringBuilder();
        foreach (var arg in args)
        {
            if (text.Length > 0)
                text.Append(' ');
            text.Append(Quote(arg));
        }
        return text.ToString();
    }

    private static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new char[] { ' ', '\t', '"', '\n' }) < 0)
            return arg;
        var text = new StringBuilder().Append('"');
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            if (c == '"')
            {
                text.Append('\\', slashes * 2 + 1);
                text.Append('"');
                slashes = 0;
                continue;
            }
            text.Append('\\', slashes);
            text.Append(c);
            slashes = 0;
        }
        text.Append('\\', slashes * 2);
        return text.Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>What <see cref="ProcessLauncher.StartDetached"/> will do, without starting it.</summary>
internal readonly record struct DetachedLaunch(bool UseShellExecute, bool NewConsole, string File, string Arguments, string WorkingDirectory);

/// <summary>The <c>SystemClock</c>. It reads the time from the engine's <see cref="TimeProvider"/> service when it has one.</summary>
public sealed class SystemClock : QObject
{
    private bool _enabled = true;
    private int _precision = (int)Precision.Seconds;
    private DateTime _date = TimeProvider.System.GetLocalNow().LocalDateTime;
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

    private DateTime Now() => (Engine?.Time ?? TimeProvider.System).GetUtcNow().LocalDateTime;

    private void Tick()
    {
        if (IsDestroyed)
            return;
        var now = Truncate(Now());
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
        var now = Now();
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
    private long _started = TimeProvider.System.GetTimestamp();

    private TimeProvider Time => Engine?.Time ?? TimeProvider.System;

    private TimeSpan Running => Time.GetElapsedTime(_started);

    public override void ComponentComplete() => _started = Time.GetTimestamp();

    [Qml] public double Elapsed() => Running.TotalSeconds;

    [Qml] public double ElapsedMs() => Running.TotalMilliseconds;

    [Qml] public double ElapsedNs() => Running.Ticks * 100.0;

    [Qml]
    public double Restart()
    {
        var s = Running.TotalSeconds;
        _started = Time.GetTimestamp();
        return s;
    }

    [Qml]
    public double RestartMs()
    {
        var s = Running.TotalMilliseconds;
        _started = Time.GetTimestamp();
        return s;
    }

    [Qml]
    public double RestartNs()
    {
        var s = Running.Ticks * 100.0;
        _started = Time.GetTimestamp();
        return s;
    }
}
