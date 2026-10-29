using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.UI.Notifications.Management;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Platform.Windows.Services.Session;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Qml;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Diagnostics;

/// <summary>
/// Reads what <see cref="Doctor"/> checks from this PC. Everything here only reads: the registry, files, windows,
/// screens and processes. <see cref="CanWrite"/> makes one empty file and removes it again.
/// </summary>
internal static unsafe partial class DoctorProbe
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    // Windows and .NET

    public static int WindowsBuild() => Environment.OSVersion.Version.Build;

    public static int WindowsUpdateRevision() => Registry.LocalMachine.OpenSubKey(CurrentVersionKey)?.GetValue("UBR") is int revision ? revision : 0;

    public static bool WindowsServer() => Registry.LocalMachine.OpenSubKey(CurrentVersionKey)?.GetValue("InstallationType") is string type
        && type.StartsWith("Server", StringComparison.OrdinalIgnoreCase);

    public static string? WindowsDisplayVersion() => Registry.LocalMachine.OpenSubKey(CurrentVersionKey)?.GetValue("DisplayVersion") as string;

    /// <summary>Whether .NET came with Winshell: then its own files sit next to Winshell's, or are bundled into it.</summary>
    public static bool SelfContained(string exePath)
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
        return string.IsNullOrEmpty(runtime)
            || string.Equals(Path.GetFullPath(runtime), Path.GetFullPath(Path.GetDirectoryName(exePath) ?? ""), StringComparison.OrdinalIgnoreCase);
    }

    // Config

    /// <summary>Reads every QML file in the config's folder and the folders under it, as the parser does when they load.</summary>
    public static Doctor.ConfigScan ParseConfig(string? rootFile)
    {
        if (rootFile is null || Path.GetDirectoryName(rootFile) is not { } dir || !Directory.Exists(dir))
            return new(0, []);
        var errors = new List<string>();
        var files = 0;
        foreach (var file in QmlFiles(dir).Take(2000))
        {
            files++;
            try
            {
                QmlParser.Parse(Preprocessor.Run(File.ReadAllText(file)), file);
            }
            catch (QmlSyntaxException e)
            {
                errors.Add(e.Message);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{file}: could not be read: {e.Message}");
            }
        }
        return new(files, errors);
    }

    /// <summary>A config's QML files, leaving out hidden folders and installed packages.</summary>
    private static IEnumerable<string> QmlFiles(string dir)
    {
        IEnumerable<string> found;
        try
        {
            found = Directory.EnumerateFiles(dir, "*.qml").Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var file in found)
            yield return file;
        string[] subfolders;
        try
        {
            subfolders = Directory.GetDirectories(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var sub in subfolders.Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.') || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                || new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            foreach (var file in QmlFiles(sub))
                yield return file;
        }
    }

    /// <summary>
    /// The error of the last load a running instance logged, or null when its last load worked. The log has
    /// "Failed to load configuration: …" or "Failed to reload configuration: …" for a failure, and "Configuration
    /// Loaded" or "Launching config" for a load that went on.
    /// </summary>
    public static string? LastLoadError(string logPath)
    {
        if (!File.Exists(logPath))
            return null;
        string text;
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return LastLoadErrorIn(text);
    }

    internal static string? LastLoadErrorIn(string log)
    {
        string? error = null;
        foreach (var line in log.Split('\n'))
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
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)pid);
        if (process == 0)
            return false;
        try
        {
            if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out var token))
                return Marshal.GetLastPInvokeError() == 5 && !Environment.IsPrivilegedProcess;
            try
            {
                int elevated;
                return GetTokenInformation(token, 20 /* TokenElevation */, &elevated, sizeof(int), out _) && elevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
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

    /// <summary>The visible top-level windows that are not minimized: where an app bar would be.</summary>
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
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var rect))
                continue;
            var length = NativeMethods.GetClassName(hwnd, name, 256);
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
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
        var argv = CommandLineToArgvW(command, out var count);
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
            LocalFree(argv);
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

    public static IReadOnlyList<(string Path, DateTime WrittenUtc)> CrashReports(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("crash-*.txt").Select(f => (f.FullName, f.LastWriteTimeUtc)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Native

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int infoClass, void* info, int length, out int returned);

    [LibraryImport("user32.dll")]
    private static partial nint GetThreadDpiAwarenessContext();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AreDpiAwarenessContextsEqual(nint a, nint b);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CommandLineToArgvW(string command, out int count);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
