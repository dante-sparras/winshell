using System.ComponentModel;
using System.Diagnostics;

namespace Winshell;

/// <summary>How a start ended. A failure carries the reason a person sees.</summary>
internal enum InstanceStartStatus
{
    /// <summary>The host ran, and the instance was closed afterwards.</summary>
    Ran,

    /// <summary><c>-d</c> started the next process and this one returned.</summary>
    Detached,

    /// <summary><c>--no-duplicate</c> found this shell already running and returned.</summary>
    Duplicate,

    /// <summary>The start could not become an instance. <see cref="InstanceResult.Reason"/> says why.</summary>
    Failed
}

/// <summary>The outcome of <see cref="InstanceStart.Run"/>.</summary>
/// <param name="Status">What the start did.</param>
/// <param name="Reason">The message a failed start reports, or null when it did not fail.</param>
internal readonly record struct InstanceResult(InstanceStartStatus Status, string? Reason)
{
    /// <summary>The host ran.</summary>
    public static InstanceResult Ran { get; } = new(InstanceStartStatus.Ran, null);

    /// <summary>This process started another and returned.</summary>
    public static InstanceResult Detached { get; } = new(InstanceStartStatus.Detached, null);

    /// <summary>This shell was already running.</summary>
    public static InstanceResult Duplicate { get; } = new(InstanceStartStatus.Duplicate, null);

    /// <summary>Reports <paramref name="reason"/> and leaves the process exit code at 1.</summary>
    public static InstanceResult Reported(string reason)
    {
        StartupError.Report(reason, null);
        Environment.ExitCode = 1;
        return new InstanceResult(InstanceStartStatus.Failed, reason);
    }
}

/// <summary>What <see cref="InstanceStart"/> needs before it can become an instance. The host supplies the rest.</summary>
/// <param name="Program">This process's program. A <c>-d</c> start launches it again.</param>
/// <param name="Arguments">The program's own command line, without the program path.</param>
/// <param name="ShellId">The configuration's id, or null to name it from where <paramref name="Program"/> is.</param>
/// <param name="DataDir">Another data folder, read as <see cref="ShellIdentity.Resolve"/> reads it.</param>
/// <param name="StateDir">Another state folder.</param>
/// <param name="CacheDir">Another cache folder.</param>
/// <param name="Log">The log the instance writes to. A shell passes <see cref="Log.Default"/>.</param>
/// <param name="Prepare">
/// Runs once the instance exists and before its pipe opens, so a crash while the pipe opens still releases what the
/// shell holds. Null when there is nothing to release yet.
/// </param>
internal sealed record InstanceLaunch(
    string Program,
    IReadOnlyList<string> Arguments,
    string? ShellId,
    string? DataDir,
    string? StateDir,
    string? CacheDir,
    Log Log,
    Action<ShellInstance>? Prepare = null);

/// <summary>
/// The instance a host runs. <see cref="Serve"/> listens and publishes, which the host calls once the shell exists.
/// </summary>
internal sealed class InstanceSession
{
    internal InstanceSession(ShellInstance instance, ShellIdentity identity, ShellArguments arguments, Log log,
        string program)
    {
        Instance = instance;
        Identity = identity;
        Arguments = arguments;
        Log = log;
        Program = program;
    }

    public ShellInstance Instance { get; }

    public ShellIdentity Identity { get; }

    public ShellArguments Arguments { get; }

    public Log Log { get; }

    public string Program { get; }

    /// <summary>Set when the host ran and the shell failed inside it. The start reports this after it closes.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Listens on the control pipe and publishes this instance, so <c>winshell list</c> can see it.</summary>
    public void Serve(IEventLoop loop, IInstanceControl control)
    {
        Instance.Listen(loop, control);
        Instance.Publish();
    }
}

/// <summary>
/// Becomes this process's instance, or leaves for a stated reason. The order is fixed: detach, wait for the shell
/// this one replaces, claim against a duplicate, open the log, open the pipe, then the host. The host calls
/// <see cref="InstanceSession.Serve"/> once its shell exists. A pipe that cannot open is abandoned. Anything that
/// opened the pipe is closed, including a host that failed.
/// </summary>
internal static class InstanceStart
{
    /// <summary>
    /// Runs the start. <paramref name="host"/> is called only when this process is the instance. A failure reports
    /// itself. A duplicate returns without a message.
    /// </summary>
    public static InstanceResult Run(InstanceLaunch launch, Action<InstanceSession> host)
    {
        var arguments = ShellArguments.Parse(launch.Arguments);
        // The parent of -d returns at once. It must not hold the mutex, or the child looks like a duplicate of it.
        // The child keeps --after and waits itself, so a reload's handoff still lets the old process go first.
        if (arguments.Daemonize)
            return Detach(launch.Program, arguments.Detached) is { } error
                ? InstanceResult.Reported(error)
                : InstanceResult.Detached;

        // A reload: the shell before this one lets go of the taskbar, the hotkeys and the tray first.
        // One that has already gone, or is still here after the wait, does not stop this start.
        if (arguments.After is { } previous)
            Instances.WaitForExit(previous);

        var locations = ShellLocations.Current;
        var identity = ShellIdentity.Resolve(launch.Program, locations, launch.ShellId, launch.DataDir,
            launch.StateDir, launch.CacheDir);
        // Held until this method returns, which is the life of the shell. A second --no-duplicate start then exits.
        using var duplicate = arguments.NoDuplicate
            ? DuplicateHold.TryClaim(locations, identity.ShellId)
            : default;
        if (arguments.NoDuplicate && !duplicate.Acquired)
            return InstanceResult.Duplicate;

        var instance = ShellInstance.Open(identity.ShellId, launch.Program, launch.Log, arguments.Verbose);
        launch.Prepare?.Invoke(instance);
        launch.Log.Info("shell",
            $"Winshell {ProductVersion.Current.Text} on Windows {Environment.OSVersion.Version}, program \"{launch.Program}\"");
        launch.Log.Info("shell", $"Shell ID: \"{identity.ShellId}\"");
        identity.CarryOver(launch.Log);
        launch.Log.Info("shell", $"Saving logs to \"{instance.LogPath}\"");
        if (instance.OpenPipe() is { } pipeError)
        {
            instance.Abandon();
            return InstanceResult.Reported(pipeError);
        }

        var session = new InstanceSession(instance, identity, arguments, launch.Log, launch.Program);
        try
        {
            host(session);
        }
        catch (Exception e) when (instance.ReportFatal(e))
        {
            // ReportFatal returns false, so this never catches: the filter runs before finally closes the log.
            throw;
        }
        finally
        {
            instance.MarkCleanExit();
            instance.Close();
        }

        return session.Failure is { } failure
            ? InstanceResult.Reported(failure.Message)
            : InstanceResult.Ran;
    }

    /// <summary>
    /// Starts <paramref name="program"/> in the background and returns at once: null when it started, or why it did not.
    /// The child supervises what <c>-d</c> asked for, so this process can exit.
    /// </summary>
    internal static string? Detach(string program, IEnumerable<string> arguments)
    {
        try
        {
            StandardHandles.KeepFromChildren();
            using var child = Process.Start(DetachedStart(program, arguments));
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return $"Could not start \"{program}\": {e.Message}";
        }
    }

    /// <summary>
    /// The start <see cref="Detach"/> makes. The window style stays normal: a window's first show takes the state its
    /// launcher asked for, and hiding it here would hide the shell.
    /// </summary>
    internal static ProcessStartInfo DetachedStart(string program, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }
}
