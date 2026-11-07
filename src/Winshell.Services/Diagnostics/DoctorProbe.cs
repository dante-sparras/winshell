using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using Winshell.Native;

namespace Winshell.Diagnostics;

/// <summary>
/// Reads what <see cref="Doctor"/> checks from this PC. Everything here only reads: the registry, files, windows,
/// screens and processes. <see cref="CanWrite"/> makes one empty file and removes it again. A configuration project is
/// not read here: <see cref="DoctorProject.Configuration"/> reads the project file itself.
/// </summary>
internal static class DoctorProbe
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static int WindowsBuild()
    {
        return Environment.OSVersion.Version.Build;
    }

    public static int WindowsUpdateRevision()
    {
        return CurrentVersionValue("UBR") is int revision ? revision : 0;
    }

    public static bool WindowsServer()
    {
        return CurrentVersionValue("InstallationType") is string type
               && type.StartsWith("Server", StringComparison.OrdinalIgnoreCase);
    }

    public static string? WindowsDisplayVersion()
    {
        return CurrentVersionValue("DisplayVersion") as string;
    }

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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
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
               || string.Equals(Path.GetFullPath(runtime), Path.GetFullPath(Path.GetDirectoryName(exePath) ?? ""),
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Why <c>preferences.toml</c> in <paramref name="configRoot"/>, where the configuration guide keeps a shell's
    /// preferences, cannot be loaded, or null when it is missing or loads.
    /// </summary>
    public static string? PreferencesProblem(string configRoot)
    {
        return string.IsNullOrEmpty(configRoot) ? null : UserFileProblem(Path.Combine(configRoot, "preferences.toml"));
    }

    /// <summary>Why this file cannot be loaded, or null when it is missing or loads.</summary>
    private static string? UserFileProblem(string path)
    {
        var load = TomlDocument.Load(path);
        return load.Kind switch
        {
            TomlRead.Missing or TomlRead.Parsed => null,
            TomlRead.Unreadable => $"{path}: could not be read: {load.Message}",
            _ => load.Describe(path)
        };
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
            using var probe = new FileStream(Path.Combine(dir.FullName, $".winshell-doctor-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
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
        return ProcessElevation.TryRead((uint)pid, out var tokenDenied) ??
               (tokenDenied && !Environment.IsPrivilegedProcess);
    }

    public static IReadOnlyList<DoctorScreens.MonitorFacts> Monitors()
    {
        return Screens.Enumerate()
            .Select(m => new DoctorScreens.MonitorFacts(m.Info.Name, m.Bounds.Left, m.Bounds.Top, m.Bounds.Right,
                m.Bounds.Bottom, m.Work.Left, m.Work.Top, m.Work.Right, m.Work.Bottom, m.Scale))
            .ToList();
    }

    /// <summary>
    /// The visible top-level windows that are not minimized or cloaked: where an app bar would be. Explorer keeps
    /// cloaked windows along the edges for touch input, which are not drawn and reserve nothing.
    /// </summary>
    public static IReadOnlyList<DoctorScreens.WindowFacts> EdgeWindows()
    {
        var result = new List<DoctorScreens.WindowFacts>();
        foreach (var hwnd in TopLevelWindows.List())
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd) || TopLevelWindows.IsCloaked(hwnd) ||
                !Win32.GetWindowRect(hwnd, out var rect))
                continue;
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            result.Add(new DoctorScreens.WindowFacts(TopLevelWindows.ClassName(hwnd), rect.Left, rect.Top, rect.Right,
                rect.Bottom, ProgramOf((int)pid)));
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
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return "";
        }
    }

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
                if (string.Equals(name, full, StringComparison.OrdinalIgnoreCase) && key.GetValue(name) is string value)
                    layers.Add(value);
        }

        return layers;
    }
}
