using Winshell.Native;
using Winshell.Updates;

namespace Winshell.Session;

/// <summary>
/// Whether this configuration runs when the current user signs in. An unpackaged Winshell is a <c>Winshell</c> value
/// under the user's <c>Run</c> key; the value starts this install's <c>winshell.exe</c> running this configuration. A
/// packaged Winshell is the package's startup task. Windows' Startup apps page lists either and can turn it off.
/// Nothing is registered until the shell sets <see cref="RunAtSignIn"/>, and Winshell never changes what that page chose.
/// </summary>
public sealed class StartupService : ObservableObject
{
    private readonly Log _log;
    private readonly IStartupRegistry? _registry;
    private bool _runs;
    private bool _elsewhere;
    private bool _turnedOff;

    internal StartupService(Log log, IStartupRegistry? registry)
    {
        _log = log;
        _registry = registry;
        Refresh();
    }

    /// <summary>
    /// The service the running shell uses: the package's startup task when packaged; for a configuration program, the
    /// <c>Run</c> value that starts it under the <c>winshell.exe</c> supervising this process; otherwise the <c>Run</c>
    /// value for this process's command. Tests construct the service with a fake instead.
    /// </summary>
    internal static StartupService ForThisProcess(Log log)
    {
        return new StartupService(log, RegistryForThisProcess());
    }

    /// <summary>
    /// The sign-in command for the configuration program <paramref name="program"/> supervised by
    /// <paramref name="supervisor"/>, or null when <paramref name="program"/> is not a configuration program or
    /// <paramref name="supervisor"/> is not <c>winshell.exe</c>.
    /// </summary>
    internal static string? SignInCommand(string program, string? supervisor)
    {
        if (SignInEntry.ProjectFolder(program) is not { } project || supervisor is null ||
            !SignInEntry.IsHost(supervisor))
            return null;
        return WindowsStartupRegistry.CommandFor(supervisor, ["run", "-p", project]);
    }

    private static IStartupRegistry? RegistryForThisProcess()
    {
        if (Packaging.IsPackaged)
            return WindowsStartupTasks.Open() is { } task ? new StartupTaskRegistry(task) : null;
        if (Environment.ProcessPath is not { } exe)
            return null;
        if (SignInEntry.ProjectFolder(exe) is null)
            return new WindowsStartupRegistry(
                WindowsStartupRegistry.CommandFor(exe, Environment.GetCommandLineArgs().Skip(1)));
        if (SignInCommand(exe, ProcessImage.SupervisingWinshell()) is not { } command)
            return null;
        return new WindowsStartupRegistry(command, bareFolder: SignInEntry.BareFolder(),
            configRoot: ConfigPaths.Root());
    }

    /// <summary>
    /// Whether this shell can register itself to run at sign-in. False when a packaged shell has no startup task, and
    /// false when this process is a configuration program that no <c>winshell.exe</c> is supervising.
    /// </summary>
    public bool Available => _registry is not null;

    /// <summary>
    /// Whether this configuration is registered to run at sign-in. Setting it adds or removes the entry, and setting
    /// it when the entry starts another install or another configuration points the entry here: this install's
    /// <c>winshell.exe run</c> of this configuration.
    /// </summary>
    public bool RunAtSignIn
    {
        get => _runs;
        set
        {
            if (_registry is null)
            {
                _log.Warn("startup",
                    "Run at sign-in needs the winshell.exe that is supervising this configuration.");
                return;
            }

            if (value == _runs)
                return;
            if (!(value ? _registry.Register() : _registry.Unregister()))
                _log.Warn("startup",
                    value
                        ? "Windows did not let Winshell register to run at sign-in."
                        : "Windows did not let Winshell stop running at sign-in.");
            Refresh();
        }
    }

    /// <summary>
    /// True when the entry is there but Windows' Startup apps page turned it off. Only that page can turn it back on.
    /// </summary>
    public bool TurnedOffInWindows => _turnedOff;

    /// <summary>
    /// True when the entry is there but starts another install or another configuration: a <c>winshell.exe</c> in
    /// another folder, as after an update unzipped somewhere new, or a different configuration.
    /// <see cref="RunAtSignIn"/> is false until it is set.
    /// </summary>
    public bool RegisteredElsewhere => _elsewhere;

    /// <summary>Reads the entry again, after the user may have changed it in Windows.</summary>
    public void Refresh()
    {
        var registered = _registry?.IsRegistered() ?? false;
        var elsewhere = registered && (_registry?.PointsElsewhere() ?? false);
        var turnedOff = registered && (_registry?.TurnedOffInWindows() ?? false);
        Set(ref _runs, registered && !elsewhere, nameof(RunAtSignIn));
        Set(ref _elsewhere, elsewhere, nameof(RegisteredElsewhere));
        Set(ref _turnedOff, turnedOff, nameof(TurnedOffInWindows));
    }
}
