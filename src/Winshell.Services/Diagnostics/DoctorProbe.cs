using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Windows.UI.Notifications.Management;
using Winshell.Native;
using Winshell.Notifications;
using Winshell.Session;
using Winshell.Windows;

namespace Winshell.Diagnostics;

/// <summary>
/// Reads what <see cref="Doctor"/> checks from this PC. Everything here only reads: the registry, files, windows,
/// screens and processes. <see cref="CanWrite"/> makes one empty file and removes it again.
/// </summary>
internal static unsafe partial class DoctorProbe
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

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

    /// <summary>
    /// Why <c>preferences.toml</c>, <c>launcher.toml</c> or <c>quick-settings.toml</c> cannot be loaded, at the paths
    /// the shell that would run reads them from. A missing file is left out.
    /// </summary>
    public static IReadOnlyList<string> UserConfigProblems(string? configFile, string configRoot, string localRoot)
    {
        var paths = new List<string?>();
        if (!string.IsNullOrEmpty(configRoot))
        {
            paths.Add(Path.Combine(configRoot, "preferences.toml"));
            paths.Add(Path.Combine(configRoot, "launcher.toml"));
        }
        if (!string.IsNullOrEmpty(localRoot))
            paths.Add(Path.Combine(localRoot, "state", "by-shell", ShellIdOf(configFile, configRoot), "quick-settings.toml"));
        var problems = new List<string>();
        foreach (var path in paths)
        {
            if (UserFileProblem(path) is { Length: > 0 } problem)
                problems.Add(problem);
        }
        return problems;
    }

    private static string ShellIdOf(string? configFile, string configRoot)
    {
        if (configFile is null || !configFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return "winshell-default";
        try
        {
            var project = ConfigurationProject.Find(configFile, null, configRoot, out _);
            return project is null ? "winshell-default" : ShellIdentity.DefaultId(project.Program, configRoot);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "winshell-default";
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
            if (DataLimits.FileBytes(stream.Length, DataLimits.MaxTextBytes) is { } why)
                return $"{path}: {why}";
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"{path}: could not be read: {e.Message}";
        }
        return TomlSyntax.TryLoad(path, text, out _, out _, out var error) ? null : error;
    }

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
        foreach (var hwnd in handles!)
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

    public static string? RunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(WindowsStartupRegistry.RunKey);
        return key?.GetValue(WindowsStartupRegistry.ValueName) as string;
    }

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

    /// <summary>Lists crash reports. A missing folder has none. A folder that cannot be listed says why.</summary>
    public static Doctor.CrashFolderRead ReadCrashReports(string folder)
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

    /// <summary>The first lines of a crash report. A file that does not start with the report heading is ignored.</summary>
    public static Doctor.CrashPeek PeekCrash(string path)
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

    [LibraryImport("user32.dll")]
    private static partial nint GetThreadDpiAwarenessContext();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AreDpiAwarenessContextsEqual(nint a, nint b);
}
