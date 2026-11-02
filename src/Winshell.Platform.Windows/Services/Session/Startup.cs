using Winshell.Runtime;
using Winshell.Session;

namespace Winshell.Platform.Windows.Services.Session;

/// <summary>
/// The <c>Startup</c>: whether Winshell runs when the current user signs in. An unpackaged Winshell is a
/// <c>Winshell</c> value under the user's <c>Run</c> key. A packaged Winshell is the package's startup task.
/// Windows' Startup apps page lists either and can turn it off. Nothing is registered until the shell sets
/// <see cref="RunAtSignIn"/>, and Winshell never changes what that page chose.
/// </summary>
public sealed class StartupService : QObject
{
    private readonly IStartupRegistry? _registry;
    private bool _runs;
    private bool _elsewhere;
    private bool _turnedOff;

    internal StartupService(QmlEngine engine)
    {
        _registry = engine.GetService<IStartupRegistry>();
        Read();
    }

    /// <summary>Whether this shell can register itself: false when it does not run as <c>winshell.exe</c>.</summary>
    [Qml] public bool Available { get { Track(); return _registry is not null; } }

    /// <summary>
    /// Whether this Winshell is registered to run at sign-in. Setting it adds or removes the entry, and setting it
    /// when the entry starts another <c>winshell.exe</c> points the entry here.
    /// </summary>
    [Qml]
    public bool RunAtSignIn
    {
        get { Track(); return _runs; }
        set
        {
            if (_registry is null)
            {
                QmlServices.Warn(this, "startup", "Startup.runAtSignIn needs a shell started by winshell.exe.");
                return;
            }
            if (value == _runs)
                return;
            if (!(value ? _registry.Register() : _registry.Unregister()))
                QmlServices.Warn(this, "startup", value ? "Windows did not let Winshell register to run at sign-in." : "Windows did not let Winshell stop running at sign-in.");
            Read();
        }
    }

    /// <summary>
    /// True when the entry is there but Windows' Startup apps page turned it off. Only that page can turn it back on.
    /// </summary>
    [Qml] public bool TurnedOffInWindows { get { Track(); return _turnedOff; } }

    /// <summary>
    /// True when the entry is there but starts Winshell another way: a <c>winshell.exe</c> in another folder, as after
    /// an update unzipped somewhere new, or other arguments. <see cref="RunAtSignIn"/> is false until it is set.
    /// </summary>
    [Qml] public bool RegisteredElsewhere { get { Track(); return _elsewhere; } }

    /// <summary>Reads the entry again, after the user may have changed it in Windows.</summary>
    [Qml]
    public void Refresh() => Read();

    private void Read()
    {
        var registered = _registry?.IsRegistered() ?? false;
        var elsewhere = registered && (_registry?.PointsElsewhere() ?? false);
        var turnedOff = registered && (_registry?.TurnedOffInWindows() ?? false);
        if (SetField(ref _runs, registered && !elsewhere, "runAtSignIn")) { }
        if (SetField(ref _elsewhere, elsewhere, "registeredElsewhere")) { }
        if (SetField(ref _turnedOff, turnedOff, "turnedOffInWindows")) { }
    }
}
