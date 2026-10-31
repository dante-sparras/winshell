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

namespace Winshell.Platform.Windows.Diagnostics;

/// <summary>
/// Reads what <see cref="Doctor"/> checks from this PC. Everything here only reads: the registry, files, windows,
/// screens and processes. <see cref="CanWrite"/> makes one empty file and removes it again.
/// </summary>
internal static unsafe partial class DoctorProbe
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    // Held around OpenVersionKey so a test can watch one open without another read seeing the replacement.
    internal static readonly object VersionKeyGate = new();

    /// <summary>Opens the Windows version key. A test replaces it, under <see cref="VersionKeyGate"/>, to see the key closed.</summary>
    internal static Func<RegistryKey?> OpenVersionKey = static () => Registry.LocalMachine.OpenSubKey(CurrentVersionKey);

    // Windows and .NET

    public static int WindowsBuild() => Environment.OSVersion.Version.Build;

    public static int WindowsUpdateRevision()
    {
        using var key = OpenVersion();
        return key?.GetValue("UBR") is int revision ? revision : 0;
    }

    public static bool WindowsServer()
    {
        using var key = OpenVersion();
        return key?.GetValue("InstallationType") is string type && type.StartsWith("Server", StringComparison.OrdinalIgnoreCase);
    }

    public static string? WindowsDisplayVersion()
    {
        using var key = OpenVersion();
        return key?.GetValue("DisplayVersion") as string;
    }

    private static RegistryKey? OpenVersion()
    {
        lock (VersionKeyGate)
            return OpenVersionKey();
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

    /// <summary>How many bytes of a log are read at once, from the end. A load line longer than this is joined across reads.</summary>
    internal const int LogChunk = 8192;

    private static readonly string[] LoadFailureMarkers =
    [
        "Failed to load configuration: ",
        "Failed to reload configuration: ",
    ];

    /// <summary>Reads the last load error. A missing log is neither an error nor unreadable.</summary>
    public static InstanceLogRead ReadInstanceLog(string logPath)
    {
        if (!File.Exists(logPath))
            return default;
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new(LastLoadErrorIn(stream), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(null, $"{logPath}: {e.Message}");
        }
    }

    public static string? LastLoadError(string logPath) => ReadInstanceLog(logPath).LoadError;

    internal static string? LastLoadErrorIn(string log)
    {
        string? error = null;
        foreach (var line in log.Split('\n'))
        {
            if (DecideLoad(line, out var next))
                error = next;
        }
        return error;
    }

    /// <summary>The last load error, read from the end of the log. The last failure or successful load decides, so earlier bytes stay unread.</summary>
    internal static string? LastLoadErrorIn(Stream stream)
    {
        foreach (var line in LinesFromEnd(stream))
        {
            if (DecideLoad(line, out var error))
                return error;
        }
        return null;
    }

    /// <summary>
    /// True when this line is a failed load or a load that went on. A load that went on clears
    /// <paramref name="error"/>, which is what a later success does to an earlier failure.
    /// </summary>
    private static bool DecideLoad(string line, out string? error)
    {
        error = null;
        var failed = false;
        foreach (var marker in LoadFailureMarkers)
        {
            var at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
                continue;
            error = line[(at + marker.Length)..].TrimEnd('\r');
            failed = true;
        }
        if (line.Contains(": Configuration Loaded", StringComparison.Ordinal) || line.Contains(": Launching config:", StringComparison.Ordinal))
        {
            error = null;
            return true;
        }
        return failed;
    }

    /// <summary>Lines of a UTF-8 log from last to first. A line that crosses a read is joined before it is decoded.</summary>
    private static IEnumerable<string> LinesFromEnd(Stream stream)
    {
        var length = stream.Length;
        if (length == 0)
            yield break;

        // Pieces of the line that is not finished yet, with the later piece first.
        var pending = new List<byte[]>();
        var pendingBytes = 0;
        var pos = length;
        var buffer = new byte[LogChunk];
        while (pos > 0)
        {
            var take = (int)Math.Min(LogChunk, pos);
            pos -= take;
            stream.Position = pos;
            ReadExact(stream, buffer, take);

            var end = take;
            while (true)
            {
                var nl = LastNewline(buffer, end);
                if (nl < 0)
                    break;
                var line = Join(buffer.AsSpan(nl + 1, end - (nl + 1)), pending);
                pending.Clear();
                pendingBytes = 0;
                if (line.Length > 0)
                    yield return line;
                end = nl;
            }

            if (end > 0)
            {
                var piece = new byte[end];
                Buffer.BlockCopy(buffer, 0, piece, 0, end);
                pending.Add(piece);
                pendingBytes += end;
            }
        }

        if (pendingBytes > 0)
        {
            var line = Join(ReadOnlySpan<byte>.Empty, pending);
            if (line.Length > 0)
                yield return line;
        }
    }

    /// <summary><paramref name="laterFirst"/> is the rest of the line toward its end, later piece first.</summary>
    private static string Join(ReadOnlySpan<byte> first, List<byte[]> laterFirst)
    {
        var count = first.Length;
        foreach (var part in laterFirst)
            count += part.Length;
        if (count == 0)
            return "";
        var bytes = new byte[count];
        first.CopyTo(bytes);
        var offset = first.Length;
        for (var i = laterFirst.Count - 1; i >= 0; i--)
        {
            var part = laterFirst[i];
            Buffer.BlockCopy(part, 0, bytes, offset, part.Length);
            offset += part.Length;
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static int LastNewline(byte[] buffer, int end)
    {
        for (var i = end - 1; i >= 0; i--)
        {
            if (buffer[i] == (byte)'\n')
                return i;
        }
        return -1;
    }

    private static void ReadExact(Stream stream, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n == 0)
                throw new EndOfStreamException();
            read += n;
        }
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
