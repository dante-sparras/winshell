using System.Text.RegularExpressions;
using Microsoft.Win32;
using global::Windows.ApplicationModel;

namespace Winshell.Session;

/// <summary>
/// The sign-in entry. Tests use a fake, or a key of their own, and never touch the user's Run key. A packaged task
/// is faked the same way: tests never call <see cref="WindowsStartupTasks.Open"/>.
/// </summary>
internal interface IStartupRegistry
{
    bool IsRegistered();

    /// <summary>Whether the entry starts something other than this command.</summary>
    bool PointsElsewhere() => false;

    bool TurnedOffInWindows();

    bool Register();

    bool Unregister();
}

/// <summary>A value under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, for the current user only.</summary>
internal sealed class WindowsStartupRegistry(string command, string runKey = WindowsStartupRegistry.RunKey, string approvedKey = WindowsStartupRegistry.ApprovedKey) : IStartupRegistry
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where the Startup apps page keeps its on and off, one binary value per Run value.</summary>
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public const string ValueName = "Winshell";

    public string Command => command;

    public bool IsRegistered() => Try(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey);
        return key?.GetValue(ValueName) is string;
    });

    public bool PointsElsewhere() => Try(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey);
        return key?.GetValue(ValueName) is string value && !SameSignInCommand(value, command);
    });

    // The page writes 12 bytes: an odd first byte is off, with the time it was turned off after it.
    public bool TurnedOffInWindows() => Try(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(approvedKey);
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    });

    public bool Register() => Try(() =>
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKey);
        key.SetValue(ValueName, command, RegistryValueKind.String);
        return true;
    });

    public bool Unregister() => Try(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        return true;
    });

    private static bool Try(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The command a sign-in runs: this install's <c>winshell.exe</c> with the arguments it was started with, under a
    /// console host that has no window. <c>winshell.exe</c> is a console program, so started on its own it would open a
    /// console or Windows Terminal at every sign-in. Start launches <c>winshellw.exe</c> in the same folder; that name
    /// is written as the sibling <c>winshell.exe</c>, so the Start menu and the installer describe one entry.
    /// <c>--no-duplicate</c> keeps a second sign-in entry from starting a second shell, and <c>--daemonize</c> is left
    /// out because the console host already runs it in the background. A <c>--path</c> is made absolute, since a
    /// sign-in does not start in this working directory.
    /// </summary>
    public static string CommandFor(string exe, IEnumerable<string> args)
    {
        exe = ConsoleTwin(exe);
        var kept = new List<string>();
        var path = false;
        foreach (var a in args)
        {
            if (a is "-d" or "--daemonize")
                continue;
            kept.Add(path ? Path.GetFullPath(a) : a);
            path = a is "-p" or "--path";
        }
        if (!kept.Any(a => a is "-n" or "--no-duplicate"))
            kept.Add("--no-duplicate");
        var conhost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");
        return string.Join(' ', new[] { conhost, "--headless", exe }.Concat(kept).Select(Quote));
    }

    /// <summary>Quotes an argument the way <c>CommandLineToArgvW</c> reads it back.</summary>
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => c is ' ' or '\t' or '"'))
            return arg;
        var sb = new System.Text.StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }

    /// <summary>The console <c>winshell.exe</c> beside a <c>winshellw.exe</c>. Any other path is unchanged.</summary>
    internal static string ConsoleTwin(string exe)
    {
        if (!Path.GetFileName(exe).Equals("winshellw.exe", StringComparison.OrdinalIgnoreCase))
            return exe;
        var dir = Path.GetDirectoryName(exe);
        return string.IsNullOrEmpty(dir) ? "winshell.exe" : Path.Combine(dir, "winshell.exe");
    }

    /// <summary><c>winshell.exe</c> and <c>winshellw.exe</c> in the same folder are one install.</summary>
    internal static bool SameInstall(string left, string right)
    {
        static bool Pair(string path)
        {
            var name = Path.GetFileName(path);
            return name.Equals("winshell.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals("winshellw.exe", StringComparison.OrdinalIgnoreCase);
        }
        if (!Pair(left) || !Pair(right))
            return false;
        return string.Equals(Path.GetDirectoryName(Path.GetFullPath(left)), Path.GetDirectoryName(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether two sign-in commands start the same install. The windowed name counts as the console name.</summary>
    internal static bool SameSignInCommand(string left, string right) =>
        string.Equals(NormalizeSignInExe(left), NormalizeSignInExe(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSignInExe(string command) =>
        Regex.Replace(command, @"winshellw\.exe(?=$|[\s""])", "winshell.exe", RegexOptions.IgnoreCase);
}

/// <summary>
/// The package's <c>windows.startupTask</c>. Windows starts the package, so there is no path to go stale and
/// nothing points elsewhere. Tests pass a fake. <see cref="WindowsStartupTasks.Open"/> is the real task.
/// </summary>
internal sealed class StartupTaskRegistry(IStartupTask task) : IStartupRegistry
{
    public bool IsRegistered() => Try(() => task.State is not StartupTaskState.Disabled);

    public bool PointsElsewhere() => false;

    public bool TurnedOffInWindows() => Try(() => task.State is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy);

    public bool Register() => Try(() =>
    {
        var state = task.State;
        if (state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
            return true;
        // DisabledByUser and DisabledByPolicy stay as Windows left them. RequestEnable would not change that.
        if (state is not StartupTaskState.Disabled)
            return false;
        var next = task.RequestEnable();
        return next is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    });

    public bool Unregister() => Try(() =>
    {
        if (task.State is StartupTaskState.Disabled)
            return true;
        // Only a task this shell enabled can be disabled here. Policy and the Startup apps page are Windows'.
        if (task.State is not StartupTaskState.Enabled)
            return false;
        return task.Disable();
    });

    private static bool Try(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return false;
        }
    }
}

/// <summary>What <see cref="StartupTaskRegistry"/> asks Windows. A test fakes it. The real calls are <see cref="WindowsStartupTasks"/>.</summary>
internal interface IStartupTask
{
    StartupTaskState State { get; }

    /// <summary>Asks Windows to enable the task. Returns the state afterwards.</summary>
    StartupTaskState RequestEnable();

    /// <summary>Turns the task off. Returns whether it is now disabled.</summary>
    bool Disable();
}

/// <summary>
/// The real <c>StartupTask</c> for task id <c>Winshell</c>. Only a packaged process calls <see cref="Open"/>, and
/// that call has no test: it would need an installed package and would change that package's sign-in.
/// </summary>
internal static class WindowsStartupTasks
{
    public const string TaskId = "Winshell";

    public static IStartupTask? Open()
    {
        try
        {
            var task = Task.Run(static async () => await StartupTask.GetAsync(TaskId).AsTask().ConfigureAwait(false)).GetAwaiter().GetResult();
            return task is null ? null : new Real(task);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    private sealed class Real(StartupTask task) : IStartupTask
    {
        public StartupTaskState State => task.State;

        // Off the caller. Completing a WinRT async call on the shell's thread can deadlock it.
        public StartupTaskState RequestEnable() =>
            Task.Run(async () => await task.RequestEnableAsync().AsTask().ConfigureAwait(false)).GetAwaiter().GetResult();

        public bool Disable()
        {
            task.Disable();
            return task.State is StartupTaskState.Disabled;
        }
    }
}
