using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Windows.UI.Notifications.Management;
using Winshell.Platform.Windows.Native;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Platform.Windows.Services.Session;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Qml;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Diagnostics;

/// <summary>
/// Reads what <see cref="Doctor"/> checks from this PC. Everything here only reads: the registry, files, windows,
/// screens and processes. <see cref="CanWrite"/> makes one empty file and removes it again.
/// </summary>
internal static unsafe partial class DoctorProbe
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // Windows and .NET

    public static int WindowsBuild() => Environment.OSVersion.Version.Build;

    public static int WindowsUpdateRevision() => CurrentVersionValue("UBR") is int revision ? revision : 0;

    public static bool WindowsServer() => CurrentVersionValue("InstallationType") is string type
        && type.StartsWith("Server", StringComparison.OrdinalIgnoreCase);

    public static string? WindowsDisplayVersion() => CurrentVersionValue("DisplayVersion") as string;

    /// <summary>
    /// Whether transparency effects are off: <c>EnableTransparency</c> is 0. Anything else, including a missing or
    /// unreadable value, is left unmentioned.
    /// </summary>
    public static bool TransparencyEffectsOff()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("EnableTransparency") is int value && value == 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Whether a contrast theme is on, the same read the theme uses.</summary>
    public static bool ContrastThemeOn()
    {
        var hc = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        return Win32.SystemParametersInfoW(Win32.SPI_GETHIGHCONTRAST, hc.cbSize, &hc, 0) && (hc.dwFlags & Win32.HCF_HIGHCONTRASTON) != 0;
    }

    private static object? CurrentVersionValue(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);
        return key?.GetValue(name);
    }

    /// <summary>Whether .NET came with Winshell: then its own files sit next to Winshell's, or are bundled into it.</summary>
    public static bool SelfContained(string exePath)
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
        return string.IsNullOrEmpty(runtime)
            || string.Equals(Path.GetFullPath(runtime), Path.GetFullPath(Path.GetDirectoryName(exePath) ?? ""), StringComparison.OrdinalIgnoreCase);
    }

    // Config

    /// <summary>How many folders reading a config walks at most, so a config in a big folder ends quickly.</summary>
    public const int MaxFolders = 500;

    /// <summary>
    /// Reads every QML file in the config's folder and the folders under it, as the parser does when they load. An
    /// error in the config's own folder is in <see cref="Doctor.ConfigScan.Errors"/>; one further down, which only
    /// matters if the config imports it, in <see cref="Doctor.ConfigScan.SubfolderErrors"/>. Hidden folders,
    /// installed packages, links and folders with a <c>shell.qml</c> of their own, which are other configs, are left out.
    /// </summary>
    public static Doctor.ConfigScan ParseConfig(string? rootFile)
    {
        if (rootFile is null || Path.GetDirectoryName(rootFile) is not { } dir || !Directory.Exists(dir))
            return new(0, []);
        var errors = new List<string>();
        var elsewhere = new List<string>();
        var files = 0;
        var walked = 0;
        var stopped = false;
        var folders = new Queue<string>([dir]);
        while (folders.TryDequeue(out var folder))
        {
            if (walked++ == MaxFolders)
            {
                stopped = true;
                break;
            }
            var sink = folder == dir ? errors : elsewhere;
            foreach (var file in List(folder, f => Directory.EnumerateFiles(f, "*.qml"), sink))
            {
                files++;
                try
                {
                    QmlParser.Parse(Preprocessor.Run(File.ReadAllText(file)), file);
                }
                catch (QmlSyntaxException e)
                {
                    sink.Add(e.Message);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    sink.Add($"{file}: could not be read: {e.Message}");
                }
            }
            foreach (var sub in List(folder, Directory.EnumerateDirectories, sink))
            {
                if (!LeftOut(sub))
                    folders.Enqueue(sub);
            }
        }
        return new(files, errors) { SubfolderErrors = elsewhere, Stopped = stopped };
    }

    /// <summary>A folder's entries in name order, or none with the reason added to the errors when it cannot be listed.</summary>
    private static List<string> List(string folder, Func<string, IEnumerable<string>> entries, List<string> errors)
    {
        try
        {
            return entries(folder).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{folder}: could not be read: {e.Message}");
            return [];
        }
    }

    private static bool LeftOut(string folder)
    {
        var name = Path.GetFileName(folder);
        if (name.StartsWith('.') || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            return new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint) || File.Exists(Path.Combine(folder, "shell.qml"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// The error of the last load a running instance logged, or null when its last load worked. The log has
    /// "Failed to load configuration: …" or "Failed to reload configuration: …" for a failure, and "Configuration
    /// Loaded" or "Launching config" for a load that went on.
    /// </summary>
    /// <summary>The last load error in an instance log, or why that log could not be read.</summary>
    public readonly record struct InstanceLogRead(string? LoadError, string? Unreadable);

    /// <summary>Reads the last load error. A missing log is neither an error nor unreadable.</summary>
    public static InstanceLogRead ReadInstanceLog(string logPath)
    {
        if (!File.Exists(logPath))
            return default;
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return new(LastLoadErrorIn(reader), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(null, $"{logPath}: {e.Message}");
        }
    }

    public static string? LastLoadError(string logPath) => ReadInstanceLog(logPath).LoadError;

    internal static string? LastLoadErrorIn(string log)
    {
        using var reader = new StringReader(log);
        return LastLoadErrorIn(reader);
    }

    /// <summary>Reads a line at a time, so a long log is never all in memory.</summary>
    private static string? LastLoadErrorIn(TextReader log)
    {
        string? error = null;
        while (log.ReadLine() is { } line)
        {
            foreach (var marker in new[] { "Failed to load configuration: ", "Failed to reload configuration: " })
            {
                var at = line.IndexOf(marker, StringComparison.Ordinal);
                if (at >= 0)
                    error = line[(at + marker.Length)..].TrimEnd('\r');
            }
            if (line.Contains(": Configuration Loaded", StringComparison.Ordinal) || line.Contains(": Launching config:", StringComparison.Ordinal))
                error = null;
        }
        return error;
    }

    /// <summary>
    /// Why <c>preferences.toml</c>, <c>launcher.toml</c> or <c>quick-settings.toml</c> cannot be loaded, at the paths
    /// this shell loads them from. A missing file is left out. The text is parsed by <see cref="TomlAdapter.TryLoad"/>.
    /// </summary>
    public static IReadOnlyList<string> UserConfigProblems(string? configFile, string configRoot, string localRoot)
    {
        var problems = new List<string>();
        foreach (var path in new[] { PreferencesPath(configFile, configRoot), LauncherPath(configFile, localRoot), QuickSettingsPath(configFile, configRoot, localRoot) })
        {
            if (UserFileProblem(path) is { Length: > 0 } problem)
                problems.Add(problem);
        }
        return problems;
    }

    /// <summary>
    /// Beside <c>shell.qml</c> when that shell lives in the config folder, and in the config folder for the default
    /// shell, which lives outside it. No config folder means the choices are not stored.
    /// </summary>
    private static string? PreferencesPath(string? configFile, string configRoot)
    {
        if (string.IsNullOrEmpty(configRoot))
            return null;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configRoot));
        if (configFile is null)
            return Path.Combine(root, "preferences.toml");
        var shellDir = Path.GetDirectoryName(Path.GetFullPath(configFile));
        if (string.IsNullOrEmpty(shellDir))
            return null;
        var shell = shellDir.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        var config = root.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        var folder = shell == config || shell.StartsWith(config + "/", StringComparison.Ordinal) ? shellDir : root;
        return Path.Combine(folder, "preferences.toml");
    }

    /// <summary>Beside <c>shell.qml</c>, or beside the default shell when Winshell would run that.</summary>
    private static string? LauncherPath(string? configFile, string localRoot)
    {
        if (configFile is not null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(configFile));
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "launcher.toml");
        }
        return string.IsNullOrEmpty(localRoot) ? null : Path.Combine(localRoot, "default-shell", "launcher.toml");
    }

    /// <summary>The shell's state folder, named the way <see cref="ShellHost"/> names it, then <c>quick-settings.toml</c>.</summary>
    private static string? QuickSettingsPath(string? configFile, string configRoot, string localRoot)
    {
        if (string.IsNullOrEmpty(localRoot))
            return null;
        var rootFile = configFile is not null
            ? Path.GetFullPath(configFile)
            : Path.Combine(localRoot, "default-shell", "shell.qml");
        string id;
        string? statePragma = null;
        try
        {
            var pragmas = ShellPragmas.Read(rootFile);
            statePragma = pragmas.StateDir;
            // The shipped default shell's id, used when that copy has not been written out yet.
            id = pragmas.ShellId ?? (configFile is null && !File.Exists(rootFile) ? "winshell-default" : ShellHost.DefaultShellId(rootFile, configRoot));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            id = configFile is null ? "winshell-default" : ShellHost.DefaultShellId(rootFile, configRoot);
        }
        var shellDir = Path.GetDirectoryName(rootFile) ?? "";
        var stateDir = ExpandState(statePragma, shellDir, Path.Combine(localRoot, "state"))
            ?? Path.Combine(localRoot, "state", "by-shell", id);
        return Path.Combine(stateDir, "quick-settings.toml");
    }

    /// <summary>The same expansion <see cref="ShellHost"/> uses for a <c>StateDir</c> pragma. A bad path is ignored.</summary>
    private static string? ExpandState(string? pragma, string shellDir, string baseDir)
    {
        if (pragma is null)
            return null;
        try
        {
            var text = Environment.ExpandEnvironmentVariables(pragma.Replace("$BASE", baseDir, StringComparison.Ordinal));
            if (text.Length == 0)
                return Path.GetFullPath(text);
            if (!Path.IsPathRooted(text))
                text = Path.Combine(shellDir, text);
            return Path.GetFullPath(text);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    /// <summary>Why this file cannot be loaded, or null when it is missing or loads.</summary>
    private static string? UserFileProblem(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"{path}: could not be read: {e.Message}";
        }
        return TomlAdapter.TryLoad(path, text, out _, out var error) ? null : error;
    }

    // Folders

    /// <summary>
    /// Whether this account can write a folder, or the folder Winshell would make it in when it is not there yet.
    /// Makes one empty file there, which is deleted as it closes.
    /// </summary>
    public static bool CanWrite(string folder)
    {
        var dir = new DirectoryInfo(folder);
        while (dir is not null && !dir.Exists)
            dir = dir.Parent;
        if (dir is null)
            return false;
        try
        {
            using var probe = new FileStream(Path.Combine(dir.FullName, $".winshell-doctor-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a process runs as administrator. A process this account may not even ask is one: a non-elevated
    /// account can always read the token of its own non-elevated processes.
    /// </summary>
    public static bool IsElevated(int pid)
    {
        var process = Win32.OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)pid);
        if (process == 0)
            return false;
        try
        {
            if (!Win32.OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out var token))
                return Marshal.GetLastPInvokeError() == 5 && !Environment.IsPrivilegedProcess;
            try
            {
                int elevated;
                return Win32.GetTokenInformation(token, 20 /* TokenElevation */, &elevated, sizeof(int), out _) && elevated != 0;
            }
            finally
            {
                Win32.CloseHandle(token);
            }
        }
        finally
        {
            Win32.CloseHandle(process);
        }
    }

    // Screens and app bars

    public static IReadOnlyList<Doctor.MonitorFacts> Monitors() => Screens.Enumerate()
        .Select(m => new Doctor.MonitorFacts(m.Info.Name, m.Bounds.Left, m.Bounds.Top, m.Bounds.Right, m.Bounds.Bottom, m.Work.Left, m.Work.Top, m.Work.Right, m.Work.Bottom, m.Scale))
        .ToList();

    [ThreadStatic]
    private static List<nint>? _windows;

    [UnmanagedCallersOnly]
    private static int CollectWindow(nint hwnd, nint data)
    {
        _windows?.Add(hwnd);
        return 1;
    }

    /// <summary>
    /// The visible top-level windows that are not minimized or cloaked: where an app bar would be. Explorer keeps
    /// cloaked windows along the edges for touch input, which are not drawn and reserve nothing.
    /// </summary>
    public static IReadOnlyList<Doctor.WindowFacts> EdgeWindows()
    {
        _windows = [];
        NativeMethods.EnumWindows(&CollectWindow, 0);
        var handles = _windows;
        _windows = null;
        var result = new List<Doctor.WindowFacts>();
        var name = stackalloc char[256];
        foreach (var hwnd in handles)
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd) || NativeMethods.IsCloaked(hwnd) || !Win32.GetWindowRect(hwnd, out var rect))
                continue;
            var length = NativeMethods.GetClassName(hwnd, name, 256);
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            result.Add(new Doctor.WindowFacts(new string(name, 0, Math.Max(0, length)), rect.Left, rect.Top, rect.Right, rect.Bottom, ProgramOf((int)pid)));
        }
        return result;
    }

    private static string ProgramOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName + ".exe";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "";
        }
    }

    // DPI

    /// <summary>Whether this process is per-monitor DPI aware, version 2, as its manifest asks unless Windows overrides it.</summary>
    public static bool PerMonitorV2() => AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), -4 /* DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 */);

    /// <summary>The compatibility settings Windows keeps for this executable, for this user and for the PC.</summary>
    public static IReadOnlyList<string> CompatibilityLayers(string exePath)
    {
        var full = Path.GetFullPath(exePath);
        var layers = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(LayersKey);
            if (key is null)
                continue;
            foreach (var name in key.GetValueNames())
            {
                if (string.Equals(name, full, StringComparison.OrdinalIgnoreCase) && key.GetValue(name) is string value)
                    layers.Add(value);
            }
        }
        return layers;
    }

    // Sign-in

    public static string? RunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(WindowsStartupRegistry.RunKey);
        return key?.GetValue(WindowsStartupRegistry.ValueName) as string;
    }

    // The Startup apps page writes 12 bytes: an odd first byte is off.
    public static bool RunEntryTurnedOff()
    {
        using var key = Registry.CurrentUser.OpenSubKey(WindowsStartupRegistry.ApprovedKey);
        return key?.GetValue(WindowsStartupRegistry.ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }

    /// <summary>The file a path names in the end, past symbolic links, or the path itself when it is no link or cannot be read.</summary>
    public static string ResolveLinks(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>The <c>winshell.exe</c> a sign-in command starts: after <c>conhost.exe --headless</c>, as Winshell writes it, or first.</summary>
    internal static string? ExeOf(string command)
    {
        var args = Split(command);
        if (args.Length >= 3 && Path.GetFileName(args[0]).Equals("conhost.exe", StringComparison.OrdinalIgnoreCase) && args[1] == "--headless")
            return args[2];
        return args.Length > 0 ? args[0] : null;
    }

    /// <summary>A command line split as Windows splits it.</summary>
    private static string[] Split(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return [];
        var argv = Win32.CommandLineToArgvW(command, out var count);
        if (argv == 0)
            return [];
        try
        {
            var args = new string[count];
            for (var i = 0; i < count; i++)
                args[i] = Marshal.PtrToStringUni(((nint*)argv)[i]) ?? "";
            return args;
        }
        finally
        {
            Win32.LocalFree(argv);
        }
    }

    // Notifications

    /// <summary>Whether Windows lets this process read notifications, asked without a prompt.</summary>
    public static string NotificationAccess()
    {
        try
        {
            return WindowsNotificationBackend.MapAccess(UserNotificationListener.Current.GetAccessStatus()).ToString();
        }
        catch (Exception e) when (WindowsNotificationBackend.IsExpectedFailure(e))
        {
            return "Unavailable";
        }
    }

    // Crash reports

    /// <summary>Crash reports in a folder, or why the folder could not be listed.</summary>
    public readonly record struct CrashFolderRead(IReadOnlyList<(string Path, DateTime WrittenUtc)> Reports, string? Unreadable);

    /// <summary>The start of one crash file. No text and no error means the file is not a Winshell crash report.</summary>
    public readonly record struct CrashPeek(string? Text, string? Unreadable);

    /// <summary>Lists crash reports. A missing folder has none. A folder that cannot be listed says why.</summary>
    public static CrashFolderRead ReadCrashReports(string folder)
    {
        if (!Directory.Exists(folder))
            return new([], null);
        try
        {
            var reports = new DirectoryInfo(folder).EnumerateFiles("crash-*.txt").Select(f => (f.FullName, f.LastWriteTimeUtc)).ToList();
            return new(reports, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new([], $"{folder}: {e.Message}");
        }
    }

    public static IReadOnlyList<(string Path, DateTime WrittenUtc)> CrashReports(string folder) =>
        ReadCrashReports(folder).Reports;

    /// <summary>The first lines of a crash report. A file that does not start with the report heading is ignored.</summary>
    public static CrashPeek PeekCrash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var first = reader.ReadLine();
            if (first is null || !first.StartsWith("Winshell crash report", StringComparison.Ordinal))
                return default;
            var text = new StringBuilder(first.Length + 256);
            text.Append(first);
            text.Append('\n');
            for (var i = 0; i < 40 && reader.ReadLine() is { } line; i++)
            {
                text.Append(line);
                text.Append('\n');
            }
            return new(text.ToString(), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(null, $"{path}: {e.Message}");
        }
    }

    // Native

    [LibraryImport("user32.dll")]
    private static partial nint GetThreadDpiAwarenessContext();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AreDpiAwarenessContextsEqual(nint a, nint b);
}
