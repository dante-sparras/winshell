using System.Globalization;
using System.Text.RegularExpressions;
using Winshell.Session;
using Winshell.Updates;

namespace Winshell.Diagnostics;

/// <summary>
/// What <see cref="Doctor.Run"/> reads from this PC. Every read stays a read: the doctor never builds, and a
/// configuration project is judged from the project file alone.
/// </summary>
internal interface IDoctorFacts
{
    int WindowsBuild();

    int WindowsUpdateRevision();

    string? WindowsDisplayVersion();

    bool WindowsServer();

    bool TransparencyEffectsOff();

    bool ContrastThemeOn();

    bool SelfContained(string exePath);

    IReadOnlyList<string> UserConfigProblems(string? configFile, string configRoot, string localRoot);

    bool CanWrite(string folder);

    bool IsElevated(int pid);

    IReadOnlyList<DoctorScreens.MonitorFacts> Monitors();

    IReadOnlyList<DoctorScreens.WindowFacts> EdgeWindows();

    bool PerMonitorV2();

    IReadOnlyList<string> CompatibilityLayers(string exePath);

    string? RunEntry();

    bool RunEntryTurnedOff();

    string ResolveLinks(string path);

    string NotificationAccess();

    Doctor.CrashFolderRead ReadCrashReports(string folder);

    Doctor.CrashPeek PeekCrash(string path);
}

/// <summary>How a <see cref="DoctorCheck"/> came out.</summary>
public enum CheckStatus
{
    /// <summary>Nothing is wrong.</summary>
    Pass,
    /// <summary>Winshell runs, but something may not work as expected.</summary>
    Warn,
    /// <summary>Something stops Winshell from working. <c>winshell doctor</c> exits with 1.</summary>
    Fail,
}

/// <summary>One thing <c>winshell doctor</c> checked: what it found and, when something is wrong, how to fix it.</summary>
/// <param name="Id">A stable name for the check, such as <c>dotnet</c>.</param>
/// <param name="Title">The check's name as <c>winshell doctor</c> prints it.</param>
/// <param name="Status">How the check came out.</param>
/// <param name="Message">What the check found.</param>
/// <param name="Fix">How to fix what is wrong, or null when nothing is or there is nothing to do.</param>
public sealed record DoctorCheck(string Id, string Title, CheckStatus Status, string Message, string? Fix = null);

/// <summary>A running Winshell, as the instance list has it.</summary>
internal sealed record DoctorInstance(string Id, int Pid, string ConfigPath, string LogPath);

/// <summary>
/// What the doctor needs from <c>winshell.exe</c>: this executable, the config that would run (or why none would),
/// the running instances and Winshell's folders.
/// </summary>
/// <param name="ExePath">The path of the running <c>winshell.exe</c>.</param>
/// <param name="ConfigRoot">The config root: <c>%APPDATA%\winshell</c>, or <c>WINSHELL_CONFIG_DIR</c> if set.</param>
/// <param name="ConfigFile">The project file that runs, or null when Winshell would run its default shell, or cannot pick one.</param>
/// <param name="ConfigProblem">Why no config can be picked, such as several configs and no <c>-c</c>; null when one can.</param>
/// <param name="Instances">The running Winshell instances.</param>
/// <param name="LocalRoot">The local root: <c>%LOCALAPPDATA%\winshell</c>, or <c>WINSHELL_LOCAL_DIR</c> if set.</param>
/// <param name="CrashRoot">The folder of crash reports, under <paramref name="LocalRoot"/>.</param>
/// <param name="ProjectRoot">The folder <see cref="Doctor.Configuration"/> searches. Null searches <paramref name="ConfigRoot"/>.</param>
internal sealed record DoctorContext(
    string ExePath,
    string ConfigRoot,
    string? ConfigFile,
    string? ConfigProblem,
    IReadOnlyList<DoctorInstance> Instances,
    string LocalRoot,
    string CrashRoot,
    string? ProjectRoot = null);

/// <summary>
/// Checks this PC's setup for Winshell. Nothing is changed: no prompt, no registry or settings write, no instance
/// started or stopped, and no build. Telling whether a folder can be written makes and removes one empty file. The
/// user's own folders are written as <c>%LOCALAPPDATA%</c>, <c>%APPDATA%</c> and <c>%USERPROFILE%</c>, so what it
/// reports can go into a bug report without the Windows user name.
/// </summary>
public static class Doctor
{
    // Each check reads what it needs through IDoctorFacts and judges it here, so the judging is tested without the PC.
    internal const string BugReports = "https://github.com/enpaze/winshell/issues/new/choose";

    internal static IReadOnlyList<DoctorCheck> Run(DoctorContext context, IDoctorFacts facts)
    {
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var search = context.ProjectRoot
            ?? (context.ConfigFile is { } file && file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(file) ?? context.ConfigRoot
                : context.ConfigRoot);
        var configFolder = search;
        var configuration = Configuration(search).ToList();
        var blocked = configuration.Any(check => check.Id == "downloaded" && check.Status == CheckStatus.Fail);
        // A reason from the CLI, such as a path that is not a project, is not what searching the folder finds.
        // A failure already found, such as a folder that cannot be read, keeps its own message.
        if (context.ConfigProblem is not null && configuration[0].Status != CheckStatus.Fail)
            configuration[0] = Project(null, context.ConfigProblem);
        else if (!blocked && configuration[0].Status != CheckStatus.Fail)
            configuration[0] = WithUserFiles(configuration[0], facts.UserConfigProblems(context.ConfigFile, context.ConfigRoot, context.LocalRoot));
        DoctorCheck[] checks =
        [
            Guard("windows", "Windows", () => Windows(facts.WindowsBuild(), facts.WindowsUpdateRevision(), facts.WindowsDisplayVersion(), facts.WindowsServer(), facts.TransparencyEffectsOff(), facts.ContrastThemeOn())),
            Guard("dotnet", ".NET", () => DotNet(Environment.Version, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, facts.SelfContained(context.ExePath))),
            .. configuration,
            Guard("folders", "Folders", () => Folders(
                [("the config folder", configFolder, facts.CanWrite(configFolder)), ("its local folder", context.LocalRoot, facts.CanWrite(context.LocalRoot))],
                context.Instances.Where(i => facts.IsElevated(i.Pid)).Select(i => i.Id).ToList(),
                Environment.IsPrivilegedProcess)),
            Guard("appBars", "App bars", () => DoctorScreens.AppBars(facts.Monitors(), facts.EdgeWindows())),
            Guard("dpi", "DPI", () => DoctorScreens.Dpi(facts.PerMonitorV2(), facts.CompatibilityLayers(context.ExePath), facts.Monitors())),
            Guard("signIn", "Sign-in", () => Packaging.IsPackaged
                ? DoctorSignIn.SignIn(WindowsStartupTasks.Open())
                : DoctorSignIn.SignIn(facts.RunEntry(), facts.RunEntryTurnedOff(), context.ExePath, File.Exists, facts.ResolveLinks)),
            Guard("notifications", "Notifications", () => Notifications(facts.NotificationAccess())),
            Guard("instances", "Running", () => Instances(context.Instances)),
            Guard("crashes", "Crashes", () => CrashesIn(facts.ReadCrashReports(context.CrashRoot), now, facts.PeekCrash)),
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

    /// <param name="build">The Windows build number, such as 26100.</param>
    /// <param name="revision">The update revision, the number after the build's dot.</param>
    /// <param name="displayVersion">The version name, such as <c>24H2</c>; null or empty leaves it out.</param>
    /// <param name="server">Whether this is Windows Server.</param>
    /// <param name="transparencyOff">Whether Windows transparency effects are off. The message says so. The result does not change.</param>
    /// <param name="contrastTheme">Whether a contrast theme is on. The message says so. The result does not change.</param>
    internal static DoctorCheck Windows(int build, int revision, string? displayVersion, bool server = false, bool transparencyOff = false, bool contrastTheme = false)
    {
        var name = server ? "Windows Server" : build >= 22000 ? "Windows 11" : "Windows 10";
        var version = string.IsNullOrEmpty(displayVersion) ? "" : " " + displayVersion;
        var message = string.Create(CultureInfo.InvariantCulture, $"{name}{version}, build {build}.{revision}");
        if (transparencyOff)
            message += ". Transparency effects are off, so Mica and Acrylic look flat";
        if (contrastTheme)
            message += ". A contrast theme is on";
        if (transparencyOff || contrastTheme)
            message += ".";
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

    /// <summary>User TOML that does not parse is a warning on the project check. A failure already says what to fix.</summary>
    private static DoctorCheck WithUserFiles(DoctorCheck check, IReadOnlyList<string>? userFiles)
    {
        if (userFiles is not { Count: > 0 })
            return check;
        var note = string.Join(" ", userFiles);
        var message = check.Message.EndsWith('.') ? $"{check.Message} {note}" : $"{check.Message}. {note}";
        if (check.Status != CheckStatus.Pass)
            return check with { Message = message };
        return check with { Status = CheckStatus.Warn, Message = message, Fix = "Correct that file and save. Winshell keeps the last values it could read until the file parses." };
    }

    /// <summary>
    /// Every check, for the shell running in this process. A configuration project's program is checked in its own
    /// folder. The default shell is checked in <paramref name="configRoot"/>. Nothing is built.
    /// </summary>
    public static IReadOnlyList<DoctorCheck> ForThisProcess(string configRoot)
    {
        var exe = Environment.ProcessPath ?? "";
        ConfigurationProject? project = null;
        try
        {
            project = ConfigurationProject.ForProgram(exe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return Run(new DoctorContext(
            exe,
            configRoot,
            project?.ProjectFile,
            null,
            ListedInstances(),
            ShellLocations.LocalRoot,
            ShellLocations.CrashRoot,
            project?.Folder), DoctorFacts.Pc);
    }

    private static List<DoctorInstance> ListedInstances()
    {
        try
        {
            return global::Winshell.Instances.List(false).Select(i => new DoctorInstance(i.Info.Id, i.Info.Pid, i.Info.ConfigPath, i.Info.Log)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Configuration project. These read the project file as data. They never build.

    /// <summary>
    /// The configuration project in <paramref name="configRoot"/>, or why there is not one: whether it was found,
    /// whether a file the build would run was downloaded, whether its program has been built, and whether a build
    /// is needed. Nothing here runs MSBuild.
    /// </summary>
    internal static IReadOnlyList<DoctorCheck> Configuration(string configRoot)
    {
        var found = FindProject(configRoot);
        return
        [
            Guard("project", "Project", () => Project(found.Project, found.Problem)),
            Guard("downloaded", "Downloaded", () => Downloaded(found.Project)),
            Guard("built", "Built", () => Built(found.Project)),
            Guard("build", "Build", () => BuildNeeded(found.Project)),
        ];
    }

    private readonly record struct FoundProject(ConfigurationProject? Project, string? Problem);

    private static FoundProject FindProject(string root)
    {
        try
        {
            var project = ConfigurationProject.Find(null, null, root, out var problem);
            return new(project, problem);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(null, $"Winshell cannot read the config folder: {e.Message}");
        }
    }

    internal static DoctorCheck Project(ConfigurationProject? project, string? problem)
    {
        if (problem is not null)
            return new("project", "Project", CheckStatus.Fail, problem, "Pick a config with winshell -c <name>, or give its folder with -p, and make sure your account can read it.");
        if (project is null)
            return new("project", "Project", CheckStatus.Pass, "No configuration project: Winshell runs its default shell.");
        return new("project", "Project", CheckStatus.Pass, project.ProjectFile);
    }

    internal static DoctorCheck Downloaded(ConfigurationProject? project)
    {
        if (project is null)
            return new("downloaded", "Downloaded", CheckStatus.Pass, "No configuration project to check.");
        if (project.Downloaded() is { } file)
            return new("downloaded", "Downloaded", CheckStatus.Fail, $"{file} was downloaded from the internet, so Winshell does not build it. It would run as you.", "If you trust it, unblock it: right-click it > Properties > Unblock, or run Unblock-File. Then save it, or start Winshell again.");
        return new("downloaded", "Downloaded", CheckStatus.Pass, "Nothing the build would run was downloaded.");
    }

    internal static DoctorCheck Built(ConfigurationProject? project)
    {
        if (project is null)
            return new("built", "Built", CheckStatus.Pass, "No configuration project to build.");
        if (!File.Exists(project.Program))
            return new("built", "Built", CheckStatus.Fail, $"{project.Name} has not been built.", "Run winshell run once to build it.");
        return new("built", "Built", CheckStatus.Pass, project.Program);
    }

    internal static DoctorCheck BuildNeeded(ConfigurationProject? project)
    {
        if (project is null)
            return new("build", "Build", CheckStatus.Pass, "No configuration project to build.");
        // A missing program is Built's failure, so it is not reported twice.
        if (!File.Exists(project.Program))
            return new("build", "Build", CheckStatus.Pass, $"Nothing to compare until {project.Name} is built.");
        if (!project.NeedsBuild())
            return new("build", "Build", CheckStatus.Pass, $"{project.Name} is up to date.");
        return new("build", "Build", CheckStatus.Warn, $"A file in {project.Name} is newer than the program.", "Run winshell run to build it. Signing in uses the last build.");
    }

    // Folders

    /// <param name="folders">Each folder to write: its name in the message, its path, whether it is writable.</param>
    /// <param name="elevatedInstances">The ids of the running instances that run as administrator.</param>
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

    /// <summary>Crash reports in a folder, or why the folder could not be listed.</summary>
    internal readonly record struct CrashFolderRead(IReadOnlyList<(string Path, DateTime WrittenUtc)> Reports, string? Unreadable);

    /// <summary>The start of one crash file. No text and no error means the file is not a Winshell crash report.</summary>
    internal readonly record struct CrashPeek(string? Text, string? Unreadable);

    internal static DoctorCheck CrashesIn(CrashFolderRead folder, DateTime nowUtc, Func<string, CrashPeek>? peek = null)
    {
        if (folder.Unreadable is not null)
            return new("crashes", "Crashes", CheckStatus.Warn, $"Could not read the crash folder: {folder.Unreadable}", "Make sure your account can read that folder, then run winshell doctor again.");
        return Crashes(folder.Reports, nowUtc, peek);
    }

    internal static DoctorCheck Crashes(IReadOnlyList<(string Path, DateTime WrittenUtc)> reports, DateTime nowUtc, Func<string, CrashPeek>? peek = null)
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

    /// <summary>The status as <c>winshell doctor</c> writes it: <c>pass</c>, <c>warn</c> or <c>fail</c>.</summary>
    public static string StatusName(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "pass",
        CheckStatus.Warn => "warn",
        _ => "fail",
    };
}
