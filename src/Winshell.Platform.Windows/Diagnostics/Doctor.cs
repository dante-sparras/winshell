using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Winshell.Platform.Windows.Diagnostics;

public enum CheckStatus
{
    Pass,
    Warn,
    Fail,
}

/// <summary>One thing <c>winshell doctor</c> checked: what it found and, when something is wrong, how to fix it.</summary>
public sealed record DoctorCheck(string Id, string Title, CheckStatus Status, string Message, string? Fix = null);

/// <summary>A running Winshell, as the instance list has it.</summary>
public sealed record DoctorInstance(string Id, int Pid, string ConfigPath, string LogPath);

/// <summary>
/// What the doctor needs from <c>winshell.exe</c>: this executable, the config that would run (or why none would),
/// the running instances and Winshell's folders.
/// </summary>
/// <param name="ConfigFile">The <c>shell.qml</c> that runs; null when Winshell would run its default shell, or cannot pick one.</param>
/// <param name="ConfigProblem">Why no config can be picked, such as several configs and no <c>-c</c>; null when one can.</param>
public sealed record DoctorContext(
    string ExePath,
    string ConfigRoot,
    string? ConfigFile,
    string? ConfigProblem,
    IReadOnlyList<DoctorInstance> Instances,
    string LocalRoot,
    string CrashRoot);

/// <summary>Runs the checks <c>winshell doctor</c> and Winshell Settings' About page show. All of them only read.</summary>
public interface IDoctor
{
    IReadOnlyList<DoctorCheck> Run();
}

/// <summary>
/// Checks this PC's setup for Winshell. Each check reads what it needs through <see cref="DoctorProbe"/> and judges
/// it here, so the judging is tested without the PC. Nothing is changed: no prompt, no registry or settings write,
/// no instance started or stopped. Telling whether a folder can be written makes and removes one empty file.
/// </summary>
public static class Doctor
{
    public const string BugReports = "https://github.com/dante-sparras/winshell/issues/new/choose";

    public static IReadOnlyList<DoctorCheck> Run(DoctorContext context)
    {
        var now = DateTime.UtcNow;
        var instance = context.ConfigFile is { } file
            ? context.Instances.FirstOrDefault(i => string.Equals(i.ConfigPath, file, StringComparison.OrdinalIgnoreCase))
            : null;
        return
        [
            Guard("windows", "Windows", () => Windows(DoctorProbe.WindowsBuild(), DoctorProbe.WindowsUpdateRevision(), DoctorProbe.WindowsDisplayVersion(), DoctorProbe.WindowsServer())),
            Guard("dotnet", ".NET", () => DotNet(Environment.Version, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, DoctorProbe.SelfContained(context.ExePath))),
            Guard("config", "Config", () => Config(context.ConfigFile, context.ConfigProblem, DoctorProbe.ParseConfig(context.ConfigFile), instance is null ? null : DoctorProbe.LastLoadError(instance.LogPath))),
            Guard("folders", "Folders", () => Folders(
                [("the config folder", context.ConfigRoot, DoctorProbe.CanWrite(context.ConfigRoot)), ("its local folder", context.LocalRoot, DoctorProbe.CanWrite(context.LocalRoot))],
                context.Instances.Where(i => DoctorProbe.IsElevated(i.Pid)).Select(i => i.Id).ToList())),
            Guard("appBars", "App bars", () => AppBars(DoctorProbe.Monitors(), DoctorProbe.EdgeWindows())),
            Guard("dpi", "DPI", () => Dpi(DoctorProbe.PerMonitorV2(), DoctorProbe.CompatibilityLayers(context.ExePath), DoctorProbe.Monitors())),
            Guard("signIn", "Sign-in", () => SignIn(DoctorProbe.RunEntry(), DoctorProbe.RunEntryTurnedOff(), context.ExePath, File.Exists)),
            Guard("notifications", "Notifications", () => Notifications(DoctorProbe.NotificationAccess())),
            Guard("instances", "Running", () => Instances(context.Instances)),
            Guard("crashes", "Crashes", () => Crashes(DoctorProbe.CrashReports(context.CrashRoot), now)),
        ];
    }

    /// <summary>A check that could not run says so, rather than ending the whole run.</summary>
    internal static DoctorCheck Guard(string id, string title, Func<DoctorCheck> check)
    {
        try
        {
            return check();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new DoctorCheck(id, title, CheckStatus.Warn, $"Could not check: {e.GetType().Name}: {e.Message}", $"Report it at {BugReports}.");
        }
    }

    // Windows

    internal static DoctorCheck Windows(int build, int revision, string? displayVersion, bool server = false)
    {
        var name = server ? "Windows Server" : build >= 22000 ? "Windows 11" : "Windows 10";
        var version = string.IsNullOrEmpty(displayVersion) ? "" : " " + displayVersion;
        var message = string.Create(CultureInfo.InvariantCulture, $"{name}{version}, build {build}.{revision}");
        if (build < 22000)
            return new("windows", "Windows", CheckStatus.Fail, message, "Winshell needs Windows 11. Upgrade this PC to Windows 11.");
        if (build < 22621)
            return new("windows", "Windows", CheckStatus.Warn, message, "Update Windows in Settings > Windows Update: Mica, Acrylic and other parts Winshell uses need Windows 11 version 22H2 or later.");
        return new("windows", "Windows", CheckStatus.Pass, message);
    }

    // .NET

    internal static DoctorCheck DotNet(Version version, string description, bool selfContained)
    {
        var how = selfContained ? "built into Winshell" : "installed on this PC";
        if (version.Major < 10)
            return new("dotnet", ".NET", CheckStatus.Fail, $"{description}, {how}", "Winshell needs .NET 10. Install the .NET 10 runtime, or use a Winshell release, which brings its own.");
        return new("dotnet", ".NET", CheckStatus.Pass, $"{description}, {how}");
    }

    // Config

    /// <summary>What reading every QML file of a config found.</summary>
    public sealed record ConfigScan(int Files, IReadOnlyList<string> Errors);

    internal static DoctorCheck Config(string? file, string? problem, ConfigScan scan, string? loadError)
    {
        if (problem is not null)
            return new("config", "Config", CheckStatus.Fail, problem, "Pick a config with winshell -c <name>, or give its folder with -p.");
        if (file is null)
            return new("config", "Config", CheckStatus.Pass, "No config of your own: Winshell runs its default shell. Copy that shell to your config folder to change it.");
        if (scan.Errors.Count > 0)
        {
            var more = scan.Errors.Count > 1 ? $" ({scan.Errors.Count - 1} more)" : "";
            return new("config", "Config", CheckStatus.Fail, $"{scan.Errors[0]}{more}", "Correct that line and save. A running Winshell loads it again, and keeps the last version that loaded until then.");
        }
        var files = scan.Files == 1 ? "1 QML file" : $"{scan.Files} QML files";
        if (loadError is not null)
            return new("config", "Config", CheckStatus.Fail, $"{file}: {files} read, but the running instance could not load it: {loadError}", "Correct that line and save. Winshell keeps the last version that loaded until then.");
        return new("config", "Config", CheckStatus.Pass, $"{file}: {files}, each reads");
    }

    // Folders

    internal static DoctorCheck Folders(IReadOnlyList<(string Name, string Path, bool Writable)> folders, IReadOnlyList<string> elevatedInstances)
    {
        var locked = folders.Where(f => !f.Writable).ToList();
        if (locked.Count > 0)
        {
            var list = string.Join("; ", locked.Select(f => $"{f.Name}, {f.Path}"));
            return new("folders", "Folders", CheckStatus.Fail, $"Winshell cannot write {list}.", "Give your account write access to it in the folder's Properties > Security, or move it with WINSHELL_CONFIG_DIR.");
        }
        if (elevatedInstances.Count > 0)
            return new("folders", "Folders", CheckStatus.Warn, $"Instance {string.Join(", ", elevatedInstances)} runs as administrator. Apps it starts run as administrator too, and other apps cannot drag onto it.", "Close it and start Winshell without Run as administrator.");
        return new("folders", "Folders", CheckStatus.Pass, $"Winshell can write {string.Join(" and ", folders.Select(f => f.Name))}");
    }

    // App bars

    /// <summary>A screen: its name, its bounds and its work area, in physical pixels.</summary>
    public sealed record MonitorFacts(string Name, int Left, int Top, int Right, int Bottom, int WorkLeft, int WorkTop, int WorkRight, int WorkBottom, double Scale);

    /// <summary>A visible top-level window: its class, its bounds in physical pixels, and its program.</summary>
    public sealed record WindowFacts(string ClassName, int Left, int Top, int Right, int Bottom, string Program);

    internal const string TaskbarClass = "Shell_TrayWnd";
    internal const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    internal const string WinshellClass = "WinshellWindow";

    internal static DoctorCheck AppBars(IReadOnlyList<MonitorFacts> monitors, IReadOnlyList<WindowFacts> windows)
    {
        var problems = new List<string>();
        var both = new List<string>();
        foreach (var m in monitors)
        {
            foreach (var (edge, reserved) in new[] { ("top", m.WorkTop - m.Top), ("bottom", m.Bottom - m.WorkBottom), ("left", m.WorkLeft - m.Left), ("right", m.Right - m.WorkRight) })
            {
                if (reserved <= 0)
                    continue;
                var along = windows.Where(w => Along(m, w, edge)).ToList();
                var taskbar = along.Where(w => w.ClassName is TaskbarClass or SecondaryTaskbarClass).Sum(w => Thickness(w, edge));
                var winshell = along.Where(w => w.ClassName == WinshellClass).Sum(w => Thickness(w, edge));
                if (taskbar > 0 && winshell > 0 && reserved >= taskbar + winshell - 2)
                    both.Add($"the {edge} of {m.Name}");
                var unexplained = reserved - taskbar - winshell;
                if (unexplained <= 2)
                    continue;
                var others = along.Where(w => w.ClassName is not (TaskbarClass or SecondaryTaskbarClass or WinshellClass)
                        && Math.Abs(Thickness(w, edge) - unexplained) <= 4)
                    .Select(w => w.Program).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var who = others.Count > 0 ? string.Join(" or ", others) : "Another app";
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{who} reserves {unexplained} pixels at the {edge} of {m.Name}"));
            }
        }
        if (problems.Count > 0)
            return new("appBars", "App bars", CheckStatus.Warn, string.Join("; ", problems) + ".", "Close that app, or turn off its option to reserve screen space. Maximized windows stop short of that edge, and a Winshell panel there sits beside it.");
        if (both.Count > 0)
            return new("appBars", "App bars", CheckStatus.Warn, $"The Windows taskbar and a Winshell panel both reserve {string.Join(" and ", both)}, so maximized windows lose both.", "Turn on Automatically hide the taskbar in Settings > Personalization > Taskbar, or put the Winshell panel on another edge.");
        return new("appBars", "App bars", CheckStatus.Pass, "No other app reserves an edge of a screen");
    }

    /// <summary>
    /// Whether a window lies in the strip a screen's edge reserves, between the edge and the work area, as app bars
    /// stacked on one edge each do.
    /// </summary>
    private static bool Along(MonitorFacts m, WindowFacts w, string edge)
    {
        var overlaps = w.Right > m.Left && w.Left < m.Right && w.Bottom > m.Top && w.Top < m.Bottom;
        return overlaps && edge switch
        {
            "top" => w.Top >= m.Top - 2 && w.Bottom <= m.WorkTop + 2,
            "bottom" => w.Top >= m.WorkBottom - 2 && w.Bottom <= m.Bottom + 2,
            "left" => w.Left >= m.Left - 2 && w.Right <= m.WorkLeft + 2,
            _ => w.Left >= m.WorkRight - 2 && w.Right <= m.Right + 2,
        };
    }

    private static int Thickness(WindowFacts w, string edge) => edge is "top" or "bottom" ? w.Bottom - w.Top : w.Right - w.Left;

    // DPI

    internal static DoctorCheck Dpi(bool perMonitorV2, IReadOnlyList<string> layers, IReadOnlyList<MonitorFacts> monitors)
    {
        var scales = string.Join(", ", monitors.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Name} at {Math.Round(m.Scale * 100)}%")));
        var flags = layers.SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(f => f != "~").ToList();
        var dpiFlags = flags.Where(f => f is "HIGHDPIAWARE" or "DPIUNAWARE" or "GDIDPISCALING").ToList();
        const string Fix = "Right-click winshell.exe, choose Properties > Compatibility > Change high DPI settings, and clear Override high DPI scaling behavior.";
        if (dpiFlags.Count > 0)
            return new("dpi", "DPI", CheckStatus.Fail, $"Windows overrides how Winshell scales ({string.Join(", ", dpiFlags)}), so it draws blurry or the wrong size. {scales}", Fix);
        if (!perMonitorV2)
            return new("dpi", "DPI", CheckStatus.Fail, $"Winshell is not per-monitor DPI aware, so it draws blurry on some screens. {scales}", Fix);
        var other = flags.Where(f => f != "RUNASADMIN").ToList();
        if (other.Count > 0)
            return new("dpi", "DPI", CheckStatus.Warn, $"Winshell runs in compatibility mode ({string.Join(", ", other)}). {scales}", "Right-click winshell.exe, choose Properties > Compatibility, and clear Run this program in compatibility mode.");
        return new("dpi", "DPI", CheckStatus.Pass, $"Per-monitor aware. {scales}");
    }

    // Sign-in

    internal static DoctorCheck SignIn(string? command, bool turnedOff, string thisExe, Func<string, bool> exists)
    {
        if (command is null)
            return new("signIn", "Sign-in", CheckStatus.Pass, "Winshell does not start when you sign in. Run Winshell when I sign in, in Winshell Settings > Startup, turns it on.");
        var exe = DoctorProbe.ExeOf(command);
        if (exe is null || !exists(exe))
            return new("signIn", "Sign-in", CheckStatus.Fail, $"The sign-in entry starts {exe ?? command}, which is not there.", "Turn Run Winshell when I sign in off and on again in Winshell Settings > Startup.");
        if (!string.Equals(Path.GetFullPath(exe), Path.GetFullPath(thisExe), StringComparison.OrdinalIgnoreCase))
            return new("signIn", "Sign-in", CheckStatus.Warn, $"The sign-in entry starts another winshell.exe: {exe}.", "Turn Run Winshell when I sign in on in Winshell Settings > Startup, from the Winshell you use.");
        if (turnedOff)
            return new("signIn", "Sign-in", CheckStatus.Warn, "Winshell is set to start when you sign in, but Startup apps in Windows turned it off.", "Turn Winshell on in Settings > Apps > Startup.");
        return new("signIn", "Sign-in", CheckStatus.Pass, "Winshell starts when you sign in");
    }

    // Notifications

    internal static DoctorCheck Notifications(string access) => access switch
    {
        "Allowed" => new("notifications", "Notifications", CheckStatus.Pass, "Windows lets Winshell read notifications"),
        "Denied" => new("notifications", "Notifications", CheckStatus.Warn, "Windows does not let Winshell read notifications, so the notification center stays empty.", "In Settings > Privacy & security > Notifications, let apps access your notifications."),
        "Unspecified" => new("notifications", "Notifications", CheckStatus.Warn, "Winshell has not asked to read notifications yet, so the notification center stays empty.", "Choose Allow notification access in Winshell Settings > Notifications."),
        _ => new("notifications", "Notifications", CheckStatus.Warn, "Windows does not let this Winshell read notifications: reading them needs an installed package with that permission. The notification center stays empty.", "Install Winshell from its package, or use Windows' notification center."),
    };

    // Running instances

    internal static DoctorCheck Instances(IReadOnlyList<DoctorInstance> instances)
    {
        if (instances.Count == 0)
            return new("instances", "Running", CheckStatus.Pass, "No Winshell is running");
        var list = string.Join("; ", instances.Select(i => string.Create(CultureInfo.InvariantCulture, $"{i.Id} (process {i.Pid}) runs {i.ConfigPath}, log {i.LogPath}")));
        return new("instances", "Running", CheckStatus.Pass, list);
    }

    // Crash reports

    internal static DoctorCheck Crashes(IReadOnlyList<(string Path, DateTime WrittenUtc)> reports, DateTime nowUtc)
    {
        var recent = reports.Where(r => nowUtc - r.WrittenUtc <= TimeSpan.FromDays(7)).OrderByDescending(r => r.WrittenUtc).ToList();
        if (recent.Count == 0)
            return new("crashes", "Crashes", CheckStatus.Pass, "No crash reports in the last 7 days");
        var count = recent.Count == 1 ? "1 crash report" : $"{recent.Count} crash reports";
        return new("crashes", "Crashes", CheckStatus.Warn, $"{count} in the last 7 days. The newest is {recent[0].Path}.", $"Read it, then attach it to a bug report at {BugReports}.");
    }

    // Output

    /// <summary>The checks as <c>winshell doctor</c> prints them: a line each, a fix under each problem, and a count.</summary>
    public static string Format(IReadOnlyList<DoctorCheck> checks, string version, bool color)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Winshell {version}: this PC's setup");
        text.AppendLine();
        var width = checks.Max(c => c.Title.Length) + 2;
        foreach (var check in checks)
        {
            var label = check.Status switch
            {
                CheckStatus.Pass => "PASS",
                CheckStatus.Warn => "WARN",
                _ => "FAIL",
            };
            if (color)
                label = (check.Status switch { CheckStatus.Pass => "\e[32m", CheckStatus.Warn => "\e[33m", _ => "\e[31m" }) + label + "\e[0m";
            text.AppendLine(CultureInfo.InvariantCulture, $"  {label}  {check.Title.PadRight(width)}{check.Message}");
            if (check.Fix is not null)
                text.AppendLine(CultureInfo.InvariantCulture, $"        {new string(' ', width)}Fix: {check.Fix}");
        }
        text.AppendLine();
        text.AppendLine(Summary(checks));
        return text.ToString();
    }

    public static string Summary(IReadOnlyList<DoctorCheck> checks)
    {
        var warns = checks.Count(c => c.Status == CheckStatus.Warn);
        var fails = checks.Count(c => c.Status == CheckStatus.Fail);
        return string.Create(CultureInfo.InvariantCulture,
            $"{checks.Count(c => c.Status == CheckStatus.Pass)} passed, {warns} {(warns == 1 ? "warning" : "warnings")}, {fails} failed.");
    }

    /// <summary>The checks as <c>winshell doctor --json</c> prints them.</summary>
    public static string ToJson(IReadOnlyList<DoctorCheck> checks, string version)
    {
        using var stream = new MemoryStream();
        // Read by people too: paths and messages are written as they are, not escaped for HTML.
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("version", version);
            json.WriteStartArray("checks");
            foreach (var check in checks)
            {
                json.WriteStartObject();
                json.WriteString("id", check.Id);
                json.WriteString("title", check.Title);
                json.WriteString("status", StatusName(check.Status));
                json.WriteString("message", check.Message);
                if (check.Fix is null)
                    json.WriteNull("fix");
                else
                    json.WriteString("fix", check.Fix);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartObject("summary");
            json.WriteNumber("pass", checks.Count(c => c.Status == CheckStatus.Pass));
            json.WriteNumber("warn", checks.Count(c => c.Status == CheckStatus.Warn));
            json.WriteNumber("fail", checks.Count(c => c.Status == CheckStatus.Fail));
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string StatusName(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "pass",
        CheckStatus.Warn => "warn",
        _ => "fail",
    };
}
