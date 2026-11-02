namespace Winshell.Session;

/// <summary>
/// Whether Winshell runs when the current user signs in. An unpackaged Winshell is a <c>Winshell</c> value under the
/// user's <c>Run</c> key. A packaged Winshell is the package's startup task. Windows' Startup apps page lists either
/// and can turn it off. Nothing is registered until the shell sets <see cref="RunAtSignIn"/>, and Winshell never
/// changes what that page chose.
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

    /// <summary>Whether this shell can register itself: false when it does not run as <c>winshell.exe</c>.</summary>
    public bool Available => _registry is not null;

    /// <summary>
    /// Whether this Winshell is registered to run at sign-in. Setting it adds or removes the entry, and setting it
    /// when the entry starts another <c>winshell.exe</c> points the entry here.
    /// </summary>
    public bool RunAtSignIn
    {
        get => _runs;
        set
        {
            if (_registry is null)
            {
                _log.Warn("startup", "RunAtSignIn needs a shell started by winshell.exe.");
                return;
            }
            if (value == _runs)
                return;
            if (!(value ? _registry.Register() : _registry.Unregister()))
                _log.Warn("startup", value ? "Windows did not let Winshell register to run at sign-in." : "Windows did not let Winshell stop running at sign-in.");
            Refresh();
        }
    }

    /// <summary>
    /// True when the entry is there but Windows' Startup apps page turned it off. Only that page can turn it back on.
    /// </summary>
    public bool TurnedOffInWindows => _turnedOff;

    /// <summary>
    /// True when the entry is there but starts Winshell another way: a <c>winshell.exe</c> in another folder, as after
    /// an update unzipped somewhere new, or other arguments. <see cref="RunAtSignIn"/> is false until it is set.
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
