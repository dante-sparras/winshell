using System.Security;
using Windows.ApplicationModel;
using Microsoft.Win32;
using Winshell.Native;

namespace Winshell.Session;

/// <summary>
/// The sign-in entry. Tests use a fake, or a key of their own, and never touch the user's Run key. A packaged task
/// is faked the same way: tests never call <see cref="WindowsStartupTasks.Open"/>.
/// </summary>
internal interface IStartupRegistry
{
    bool IsRegistered();

    /// <summary>Whether the entry starts something other than this command.</summary>
    bool PointsElsewhere();

    bool TurnedOffInWindows();

    bool Register();

    bool Unregister();
}

/// <summary>A value under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, for the current user only.</summary>
/// <param name="command">The command this entry starts.</param>
/// <param name="configRoot">The config folder a sign-in command's <c>-c</c>, or its lack of <c>-p</c> and <c>-c</c>, is read against.</param>
/// <param name="runKey">The <c>Run</c> key, or a key of a test's own.</param>
/// <param name="approvedKey">Where Startup apps keeps its on and off for <paramref name="runKey"/>.</param>
internal sealed class WindowsStartupRegistry(
    string command,
    string configRoot,
    string runKey = SignInEntry.RunKey,
    string approvedKey = SignInEntry.ApprovedKey) : IStartupRegistry
{
    public string Command => command;

    public bool IsRegistered()
    {
        return Try(() => SignInEntry.RegisteredCommand(runKey) is not null);
    }

    public bool PointsElsewhere()
    {
        return Try(() => SignInEntry.RegisteredCommand(runKey) is { } value && !Starts(value));
    }

    public bool TurnedOffInWindows()
    {
        return Try(() => SignInEntry.TurnedOffInWindows(approvedKey));
    }

    public bool Register()
    {
        return Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(runKey);
            key.SetValue(SignInEntry.Name, command, RegistryValueKind.String);
            return true;
        });
    }

    public bool Unregister()
    {
        return Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKey, true);
            key?.DeleteValue(SignInEntry.Name, false);
            return true;
        });
    }

    private static bool Try(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException
                                      or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="actual"/> starts what this entry's command starts. The windowed name counts as the
    /// console name. When the command names a configuration, <paramref name="actual"/> starts it when it runs the
    /// same install and <c>winshell run</c> would choose the same project for it, read now: adding a configuration
    /// while a shell is open changes which project the installer's command, with no <c>-p</c> and no <c>-c</c>, starts.
    /// </summary>
    private bool Starts(string actual)
    {
        var wanted = SignInEntry.Read(command);
        if (!wanted.NamesConfiguration)
            return Words(actual).SequenceEqual(Words(command), StringComparer.OrdinalIgnoreCase);
        var seen = SignInEntry.Read(actual);
        return wanted.Program is { } here && seen.Program is { } started &&
               SignInEntry.SameInstall(started, here, SignInEntry.Resolve) &&
               SignInEntry.ProjectOf(wanted, configRoot) is { } project &&
               SignInEntry.Starts(seen, project, configRoot);
    }

    private static IEnumerable<string> Words(string command)
    {
        return CommandLine.Split(command).Select(WinshellHost.ConsoleOf);
    }
}

/// <summary>
/// The package's <c>windows.startupTask</c>. Windows starts the package, so there is no path to go stale and
/// nothing points elsewhere. Tests pass a fake. <see cref="WindowsStartupTasks.Open"/> is the real task.
/// </summary>
internal sealed class StartupTaskRegistry(IStartupTask task) : IStartupRegistry
{
    public bool IsRegistered()
    {
        return Try(() => task.State is not StartupTaskState.Disabled);
    }

    public bool PointsElsewhere()
    {
        return false;
    }

    public bool TurnedOffInWindows()
    {
        return Try(() => task.State is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy);
    }

    public bool Register()
    {
        return Try(() =>
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
    }

    public bool Unregister()
    {
        return Try(() =>
        {
            if (task.State is StartupTaskState.Disabled)
                return true;
            // Only a task this shell enabled can be disabled here. Policy and the Startup apps page are Windows'.
            if (task.State is not StartupTaskState.Enabled)
                return false;
            return task.Disable();
        });
    }

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
/// The real <c>StartupTask</c> that <see cref="SignInEntry.OpenTask"/> reads. Enabling or disabling it has no test:
/// that would change an installed package's sign-in.
/// </summary>
internal static class WindowsStartupTasks
{
    public static IStartupTask? Open()
    {
        return SignInEntry.OpenTask() is { } task ? new Real(task) : null;
    }

    private sealed class Real(StartupTask task) : IStartupTask
    {
        public StartupTaskState State => task.State;

        // Off the caller. Completing a WinRT async call on the shell's thread can deadlock it.
        public StartupTaskState RequestEnable() =>
#pragma warning disable VSTHRD002 // Task.Run leaves the caller, so its context is not what it waits for.
            Task.Run(async () => await task.RequestEnableAsync().AsTask().ConfigureAwait(false)).GetAwaiter()
                .GetResult();
#pragma warning restore VSTHRD002

        public bool Disable()
        {
            task.Disable();
            return task.State is StartupTaskState.Disabled;
        }
    }
}
