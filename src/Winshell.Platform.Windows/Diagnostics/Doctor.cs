using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Winshell.Platform.Windows.Services.Session;
using Winshell.Platform.Windows.Services.Updates;
using Winshell.Runtime;
using Winshell.Runtime.Shell;
using StartupTaskState = global::Windows.ApplicationModel.StartupTaskState;

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
/// no instance started or stopped. Telling whether a folder can be written makes and removes one empty file. The
/// user's own folders are written as <c>%LOCALAPPDATA%</c>, <c>%APPDATA%</c> and <c>%USERPROFILE%</c>, so what it
/// prints can go into a bug report without the Windows user name.
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
        var instanceLog = instance is null ? default : DoctorProbe.ReadInstanceLog(instance.LogPath);
        var configFolder = Path.GetDirectoryName(context.ConfigFile) is { Length: > 0 } own ? own : context.ConfigRoot;
        // A downloaded config does not run, so its files are not read either.
        var downloaded = context.ConfigFile is { } root && ZoneIdentifier.IsDownloaded(root);
        DoctorCheck[] checks =
        [
            Guard("windows", "Windows", () => Windows(DoctorProbe.WindowsBuild(), DoctorProbe.WindowsUpdateRevision(), DoctorProbe.WindowsDisplayVersion(), DoctorProbe.WindowsServer())),
            Guard("dotnet", ".NET", () => DotNet(Environment.Version, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, DoctorProbe.SelfContained(context.ExePath))),
            Guard("config", "Config", () => Config(context.ConfigFile, context.ConfigProblem, downloaded ? new ConfigScan(0, []) : DoctorProbe.ParseConfig(context.ConfigFile), instanceLog.LoadError, instanceLog.Unreadable, downloaded)),
            Guard("folders", "Folders", () => Folders(
                [("the config folder", configFolder, DoctorProbe.CanWrite(configFolder)), ("its local folder", context.LocalRoot, DoctorProbe.CanWrite(context.LocalRoot))],
                context.Instances.Where(i => DoctorProbe.IsElevated(i.Pid)).Select(i => i.Id).ToList(),
                Environment.IsPrivilegedProcess)),
            Guard("appBars", "App bars", () => AppBars(DoctorProbe.Monitors(), DoctorProbe.EdgeWindows())),
            Guard("dpi", "DPI", () => Dpi(DoctorProbe.PerMonitorV2(), DoctorProbe.CompatibilityLayers(context.ExePath), DoctorProbe.Monitors())),
            Guard("signIn", "Sign-in", () => Packaging.IsPackaged
                ? SignIn(WindowsStartupTasks.Open())
                : SignIn(DoctorProbe.RunEntry(), DoctorProbe.RunEntryTurnedOff(), context.ExePath, File.Exists, DoctorProbe.ResolveLinks)),
            Guard("notifications", "Notifications", () => Notifications(DoctorProbe.NotificationAccess())),
            Guard("instances", "Running", () => Instances(context.Instances)),
            Guard("crashes", "Crashes", () => CrashesIn(DoctorProbe.ReadCrashReports(context.CrashRoot), now, DoctorProbe.PeekCrash)),
        ];
        return WithoutUserFolders(checks,
        [
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
        ]);
    }

    /// <summary>
    /// Writes each of the user's folders as its variable in every message and fix, the longest folder first. A
    /// folder only matches whole: <c>C:\Users\ann</c> leaves <c>C:\Users\anna</c> alone.
    /// </summary>
    internal static IReadOnlyList<DoctorCheck> WithoutUserFolders(IReadOnlyList<DoctorCheck> checks, IReadOnlyList<(string Path, string Name)> folders)
    {
        var patterns = folders.Where(f => f.Path.Length > 0).OrderByDescending(f => f.Path.Length)
            .Select(f => (Pattern: new Regex(Regex.Escape(f.Path.TrimEnd('\\')) + @"(?![\w\-]|\.\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), f.Name))
            .ToList();
        string Replace(string text) => patterns.Aggregate(text, (t, p) => p.Pattern.Replace(t, _ => p.Name));
        return checks.Select(c => c with { Message = Replace(c.Message), Fix = c.Fix is null ? null : Replace(c.Fix) }).ToList();
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

    /// <summary>What reading the QML files of a config found.</summary>
    /// <param name="Errors">What does not parse in the root file's own folder, whose files Winshell loads.</param>
    public sealed record ConfigScan(int Files, IReadOnlyList<string> Errors)
    {
        /// <summary>What does not parse in a folder under it, which only matters if the config imports it.</summary>
        public IReadOnlyList<string> SubfolderErrors { get; init; } = [];

        /// <summary>Whether reading stopped at <see cref="DoctorProbe.MaxFolders"/> folders, leaving the rest unread.</summary>
        public bool Stopped { get; init; }
    }

    /// <param name="downloaded">Whether the config's <c>shell.qml</c> was downloaded and is not unblocked, so Winshell does not run it.</param>
    internal static DoctorCheck Config(string? file, string? problem, ConfigScan scan, string? loadError, string? logUnreadable = null, bool downloaded = false)
    {
        if (problem is not null)
            return new("config", "Config", CheckStatus.Fail, problem, "Pick a config with winshell -c <name>, or give its folder with -p, and make sure your account can read it.");
        if (file is null)
            return new("config", "Config", CheckStatus.Pass, "No config of your own: Winshell runs its default shell. Copy that shell to your config folder to change it.");
        if (downloaded)
            return new("config", "Config", CheckStatus.Fail, $"{file} was downloaded from the internet, so Winshell does not run it. It would run as you.", "If you trust it, unblock it: right-click it > Properties > Unblock, or run Unblock-File. Then save it, or start Winshell again.");
        if (scan.Errors.Count > 0)
        {
            var more = scan.Errors.Count > 1 ? $" ({scan.Errors.Count - 1} more)" : "";
            return new("config", "Config", CheckStatus.Fail, $"{scan.Errors[0]}{more}", "Correct that line and save. A running Winshell loads it again, and keeps the last version that loaded until then.");
        }
        var files = scan.Files == 1 ? "1 QML file" : $"{scan.Files} QML files";
        if (loadError is not null)
            return new("config", "Config", CheckStatus.Fail, $"{file}: {files} read, but the running instance could not load it: {loadError}", "Correct that line and save. Winshell keeps the last version that loaded until then.");
        var stopped = scan.Stopped ? $" Stopped after {DoctorProbe.MaxFolders} folders; the rest were not read." : "";
        if (scan.SubfolderErrors.Count > 0)
        {
            var more = scan.SubfolderErrors.Count > 1 ? $" ({scan.SubfolderErrors.Count - 1} more)" : "";
            return new("config", "Config", CheckStatus.Warn, $"{scan.SubfolderErrors[0]}{more}. It does not parse; Winshell only fails if the config imports it.{stopped}", "Correct that line, or delete the file if nothing uses it.");
        }
        if (logUnreadable is not null)
            return new("config", "Config", CheckStatus.Warn, $"Could not read the running instance's log: {logUnreadable}", "Make sure your account can read that log, then run winshell doctor again.");
        return new("config", "Config", CheckStatus.Pass, $"{file}: {files} read without errors{(stopped.Length > 0 ? "." + stopped : "")}");
    }

    // Folders

    /// <param name="asAdministrator">Whether the doctor itself runs as administrator, and so says what an administrator can write.</param>
    internal static DoctorCheck Folders(IReadOnlyList<(string Name, string Path, bool Writable)> folders, IReadOnlyList<string> elevatedInstances, bool asAdministrator = false)
    {
        var who = asAdministrator ? " This ran as administrator, so it says what an administrator can write. Run it without Run as administrator to check your account." : "";
        var locked = folders.Where(f => !f.Writable).ToList();
        if (locked.Count > 0)
        {
            var list = string.Join("; ", locked.Select(f => $"{f.Name}, {f.Path}"));
            return new("folders", "Folders", CheckStatus.Fail, $"Winshell cannot write {list}.{who}", "Give your account write access to it in the folder's Properties > Security, or move it with WINSHELL_CONFIG_DIR.");
        }
        if (elevatedInstances.Count > 0)
            return new("folders", "Folders", CheckStatus.Warn, $"Instance {string.Join(", ", elevatedInstances)} runs as administrator. Apps it starts run as administrator too, and other apps cannot drag onto it.{who}", "Close it and start Winshell without Run as administrator.");
        return new("folders", "Folders", CheckStatus.Pass, $"Winshell can write {string.Join(" and ", folders.Select(f => f.Name))}{(who.Length > 0 ? "." + who : "")}");
    }

    // App bars

    /// <summary>A screen: its name, its bounds and its work area, in physical pixels.</summary>
    public sealed record MonitorFacts(string Name, int Left, int Top, int Right, int Bottom, int WorkLeft, int WorkTop, int WorkRight, int WorkBottom, double Scale);

    /// <summary>A visible top-level window: its class, its bounds in physical pixels, and its program.</summary>
    public sealed record WindowFacts(string ClassName, int Left, int Top, int Right, int Bottom, string Program);

    internal const string TaskbarClass = "Shell_TrayWnd";
    internal const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    internal const string WinshellClass = "WinshellWindow";

    /// <summary>How thick a window must be to be named as an app bar beside a Winshell panel; thinner ones are borders and edge helpers.</summary>
    private const int MinAppBar = 8;

    internal static DoctorCheck AppBars(IReadOnlyList<MonitorFacts> monitors, IReadOnlyList<WindowFacts> windows)
    {
        var problems = new List<string>();
        var both = new List<string>();
        foreach (var m in monitors)
        {
            var onScreen = windows.Where(w => w.Right > m.Left && w.Left < m.Right && w.Bottom > m.Top && w.Top < m.Bottom).ToList();
            foreach (var (edge, reserved) in new[] { ("top", m.WorkTop - m.Top), ("bottom", m.Bottom - m.WorkBottom), ("left", m.WorkLeft - m.Left), ("right", m.Right - m.WorkRight) })
            {
                if (reserved <= 0)
                    continue;
                var taskbars = onScreen.Where(w => w.ClassName is TaskbarClass or SecondaryTaskbarClass && Within(Depth(m, w, edge), reserved)).ToList();
                // A Winshell panel also reserves its margins, which its window does not cover, and its zone can differ
                // from its size. A window that starts in the strip explains those pixels, but another app's window
                // in the same strip is still named, even when it overlaps the panel and nothing is left over.
                var winshell = onScreen.Where(w => w.ClassName == WinshellClass && Starts(m, Depth(m, w, edge), edge, reserved)).ToList();
                var taskbar = Covered(taskbars.Select(w => Depth(m, w, edge)), reserved);
                var unexplained = reserved - Covered(taskbars.Concat(winshell).Select(w => Depth(m, w, edge)), reserved);
                if (taskbar > 0 && winshell.Count > 0 && reserved > taskbar + 2)
                    both.Add($"the {edge} of {m.Name}");
                if (unexplained <= 2 && winshell.Count == 0)
                    continue;
                var others = onScreen.Where(w => w.ClassName is not (TaskbarClass or SecondaryTaskbarClass or WinshellClass) && w.Program.Length > 0)
                    .Select(w => (w.Program, Depth: Depth(m, w, edge)))
                    .Where(w => Within(w.Depth, reserved))
                    .Select(w => (w.Program, Thickness: w.Depth.End - w.Depth.Start))
                    .Where(w => winshell.Count > 0 ? w.Thickness >= MinAppBar : Math.Abs(w.Thickness - unexplained) <= 4)
                    .Select(w => w.Program).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (winshell.Count > 0)
                {
                    if (others.Count > 0)
                        problems.Add($"{string.Join(" or ", others)} reserves space at the {edge} of {m.Name}");
                    continue;
                }
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

    /// <summary>How far into the screen from an edge a window starts and ends.</summary>
    private static (int Start, int End) Depth(MonitorFacts m, WindowFacts w, string edge) => edge switch
    {
        "top" => (w.Top - m.Top, w.Bottom - m.Top),
        "bottom" => (m.Bottom - w.Bottom, m.Bottom - w.Top),
        "left" => (w.Left - m.Left, w.Right - m.Left),
        _ => (m.Right - w.Right, m.Right - w.Left),
    };

    /// <summary>Whether a window lies in the strip an edge reserves, as app bars stacked on one edge each do.</summary>
    private static bool Within((int Start, int End) depth, int reserved) => depth.Start >= -2 && depth.End <= reserved + 2;

    /// <summary>Whether a window starts in the strip an edge reserves and reaches less than halfway across the screen, as a panel does.</summary>
    private static bool Starts(MonitorFacts m, (int Start, int End) depth, string edge, int reserved) =>
        depth.Start >= -2 && depth.Start < reserved && depth.End <= (edge is "top" or "bottom" ? m.Bottom - m.Top : m.Right - m.Left) / 2;

    /// <summary>How much of the strip the windows cover together: windows side by side on one edge count once.</summary>
    private static int Covered(IEnumerable<(int Start, int End)> depths, int reserved)
    {
        var covered = 0;
        var reach = 0;
        foreach (var (start, end) in depths.Select(d => (Start: Math.Max(0, d.Start), End: Math.Min(reserved, d.End))).Where(d => d.End > d.Start).OrderBy(d => d.Start))
        {
            var from = Math.Max(start, reach);
            if (end <= from)
                continue;
            covered += end - from;
            reach = end;
        }
        return covered;
    }

    // DPI

    internal static DoctorCheck Dpi(bool perMonitorV2, IReadOnlyList<string> layers, IReadOnlyList<MonitorFacts> monitors)
    {
        var scales = string.Join(", ", monitors.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Name} at {Math.Round(m.Scale * 100)}%")));
        var flags = layers.SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(f => f != "~").ToList();
        const string HighDpi = "Right-click winshell.exe, choose Properties > Compatibility > Change high DPI settings, and clear";
        var unaware = flags.Where(f => f is "DPIUNAWARE" or "GDIDPISCALING").ToList();
        if (!perMonitorV2 || unaware.Count > 0)
        {
            var cause = unaware.Count > 0 ? $"Windows overrides how Winshell scales ({string.Join(", ", unaware)})" : "Winshell is not per-monitor DPI aware";
            return new("dpi", "DPI", CheckStatus.Fail, $"{cause}, so it draws blurry or the wrong size. {scales}", $"{HighDpi} Override high DPI scaling behavior.");
        }
        var messages = new List<string>();
        var fixes = new List<string>();
        // "Scaling performed by: Application" and "Program DPI": Winshell already scales itself on each screen.
        var highDpi = flags.Where(f => f is "HIGHDPIAWARE" or "PERPROCESSSYSTEMDPIFORCEON" or "PERPROCESSSYSTEMDPIFORCEOFF").ToList();
        if (highDpi.Count > 0)
        {
            messages.Add($"Windows keeps high DPI settings for winshell.exe ({string.Join(", ", highDpi)}). Winshell scales itself on each screen, so they can only get in its way.");
            var boxes = new List<string>();
            if (highDpi.Contains("HIGHDPIAWARE"))
                boxes.Add("Override high DPI scaling behavior");
            if (highDpi.Any(f => f.StartsWith("PERPROCESSSYSTEMDPI", StringComparison.Ordinal)))
                boxes.Add("Use this setting to fix scaling problems for this program instead of the one in Settings");
            fixes.Add($"{HighDpi} {string.Join(" and ", boxes)}.");
        }
        if (flags.Contains("RUNASADMIN"))
        {
            messages.Add("Windows always starts winshell.exe as administrator, so it asks first, and skips it when you sign in.");
            fixes.Add("Right-click winshell.exe, choose Properties > Compatibility, and clear Run this program as an administrator.");
        }
        var other = flags.Except(highDpi).Where(f => f != "RUNASADMIN").ToList();
        if (other.Count > 0)
        {
            messages.Add($"Winshell runs in compatibility mode ({string.Join(", ", other)}).");
            fixes.Add("Right-click winshell.exe, choose Properties > Compatibility, and clear Run this program in compatibility mode.");
        }
        if (messages.Count > 0)
            return new("dpi", "DPI", CheckStatus.Warn, $"{string.Join(" ", messages)} {scales}", string.Join(" ", fixes));
        return new("dpi", "DPI", CheckStatus.Pass, $"Per-monitor aware. {scales}");
    }

    // Sign-in

    /// <param name="resolve">The file a path names in the end, past links: a winget install starts through one.</param>
    internal static DoctorCheck SignIn(string? command, bool turnedOff, string thisExe, Func<string, bool> exists, Func<string, string>? resolve = null)
    {
        resolve ??= path => path;
        if (command is null)
            return new("signIn", "Sign-in", CheckStatus.Pass, "Winshell does not start when you sign in. Run Winshell when I sign in, in Winshell Settings > Startup, turns it on.");
        var exe = DoctorProbe.ExeOf(command);
        if (exe is null || !exists(exe))
            return new("signIn", "Sign-in", CheckStatus.Fail, $"The sign-in entry starts {exe ?? command}, which is not there.", "Turn Run Winshell when I sign in off and on again in Winshell Settings > Startup.");
        var started = resolve(Path.GetFullPath(exe));
        var here = resolve(Path.GetFullPath(thisExe));
        // Start launches winshellw.exe. Sign-in runs the winshell.exe beside it. They are one install.
        if (!string.Equals(started, here, StringComparison.OrdinalIgnoreCase) && !WindowsStartupRegistry.SameInstall(started, here))
            return new("signIn", "Sign-in", CheckStatus.Warn, $"The sign-in entry starts another winshell.exe: {exe}.", "Turn Run Winshell when I sign in on in Winshell Settings > Startup, from the Winshell you use.");
        if (turnedOff)
            return new("signIn", "Sign-in", CheckStatus.Warn, "Winshell is set to start when you sign in, but Startup apps in Windows turned it off.", "Turn Winshell on in Settings > Apps > Startup.");
        return new("signIn", "Sign-in", CheckStatus.Pass, "Winshell starts when you sign in");
    }

    /// <summary>Sign-in for a packaged Winshell: the startup task, which names no executable path.</summary>
    internal static DoctorCheck SignIn(IStartupTask? task)
    {
        if (task is null)
            return new("signIn", "Sign-in", CheckStatus.Warn, "This package has no startup task, so Winshell cannot run when you sign in.");
        return task.State switch
        {
            StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy =>
                new("signIn", "Sign-in", CheckStatus.Pass, "Winshell starts when you sign in"),
            StartupTaskState.DisabledByUser =>
                new("signIn", "Sign-in", CheckStatus.Warn, "Winshell is set to start when you sign in, but Startup apps in Windows turned it off.", "Turn Winshell on in Settings > Apps > Startup."),
            StartupTaskState.DisabledByPolicy =>
                new("signIn", "Sign-in", CheckStatus.Warn, "Winshell is set to start when you sign in, but Windows policy turned it off.", "A Windows policy controls this. Startup apps in Settings cannot turn it on."),
            _ => new("signIn", "Sign-in", CheckStatus.Pass, "Winshell does not start when you sign in. Run Winshell when I sign in, in Winshell Settings > Startup, turns it on."),
        };
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

    internal static DoctorCheck CrashesIn(DoctorProbe.CrashFolderRead folder, DateTime nowUtc, Func<string, DoctorProbe.CrashPeek>? peek = null)
    {
        if (folder.Unreadable is not null)
            return new("crashes", "Crashes", CheckStatus.Warn, $"Could not read the crash folder: {folder.Unreadable}", "Make sure your account can read that folder, then run winshell doctor again.");
        return Crashes(folder.Reports, nowUtc, peek);
    }

    internal static DoctorCheck Crashes(IReadOnlyList<(string Path, DateTime WrittenUtc)> reports, DateTime nowUtc, Func<string, DoctorProbe.CrashPeek>? peek = null)
    {
        var recent = reports.Where(r => nowUtc - r.WrittenUtc <= TimeSpan.FromDays(7)).OrderByDescending(r => r.WrittenUtc).ToList();
        if (peek is null)
        {
            if (recent.Count == 0)
                return new("crashes", "Crashes", CheckStatus.Pass, "No crash reports in the last 7 days");
            var count = recent.Count == 1 ? "1 crash report" : $"{recent.Count} crash reports";
            return new("crashes", "Crashes", CheckStatus.Warn, $"{count} in the last 7 days. The newest is {recent[0].Path}.", $"Read it, then attach it to a bug report at {BugReports}.");
        }

        var opened = new List<(string Path, string Text)>();
        string? unreadable = null;
        foreach (var report in recent)
        {
            var head = peek(report.Path);
            if (head.Unreadable is not null)
            {
                unreadable ??= head.Unreadable;
                continue;
            }
            if (head.Text is null)
                continue;
            opened.Add((report.Path, head.Text));
        }
        if (unreadable is not null && opened.Count == 0)
            return new("crashes", "Crashes", CheckStatus.Warn, $"Could not read a crash report: {unreadable}", $"Read the crash folder, then attach a report to a bug report at {BugReports}.");
        if (opened.Count == 0)
            return new("crashes", "Crashes", CheckStatus.Pass, "No crash reports in the last 7 days");

        var countText = opened.Count == 1 ? "1 crash report" : $"{opened.Count} crash reports";
        var message = $"{countText} in the last 7 days. The newest is {opened[0].Path}.";
        var fatal = opened.FirstOrDefault(r => r.Text.Contains("Winshell closed: yes", StringComparison.Ordinal));
        if (fatal.Path is not null)
        {
            message += " Winshell closed: yes.";
            var line = ExceptionLine(fatal.Text);
            if (line is not null)
                message += " " + line;
            if (!string.Equals(fatal.Path, opened[0].Path, StringComparison.OrdinalIgnoreCase))
                message += $" That report is {fatal.Path}.";
        }
        else if (ExceptionLine(opened[0].Text) is { } line)
        {
            message += " " + line;
        }
        if (unreadable is not null)
            message += $" Could not read a crash report: {unreadable}.";
        return new("crashes", "Crashes", CheckStatus.Warn, message, $"Read it, then attach it to a bug report at {BugReports}.");
    }

    private static string? ExceptionLine(string head)
    {
        var lines = head.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!string.Equals(lines[i].TrimEnd('\r'), "Exception", StringComparison.Ordinal) || i + 1 >= lines.Length)
                continue;
            var line = lines[i + 1].TrimEnd('\r');
            if (line.Length > 0)
                return line;
        }
        return null;
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
        using (var json = new Utf8JsonWriter(stream, JsonText.IndentedWriter))
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
