using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.ExplorerTaskbar;

/// <summary>
/// One process-wide handoff. <see cref="Hide"/> is the first time the taskbar object exists, so leaving this off
/// never looks at Explorer's windows. <see cref="Restore"/> is safe to call again, including after a crash handler
/// and the process exit have both run.
/// </summary>
public sealed class ExplorerTaskbarSession : IDisposable
{
    private readonly Func<IExplorerTaskbar> _create;
    private readonly object _gate = new();
    private IExplorerTaskbar? _taskbar;
    private bool _hidden;

    internal ExplorerTaskbarSession(Func<IExplorerTaskbar> create) => _create = create;

    /// <summary>The session the running shell uses. Tests construct a session with a fake instead.</summary>
    public static ExplorerTaskbarSession ForThisProcess() => new(() => new Win32ExplorerTaskbar());

    public bool Hidden
    {
        get { lock (_gate) return _hidden; }
    }

    public void Hide()
    {
        lock (_gate)
        {
            if (_hidden)
                return;
            _taskbar ??= _create();
            _hidden = true;
            try
            {
                _taskbar.Hide();
            }
            catch
            {
                try
                {
                    _taskbar.Restore();
                    _hidden = false;
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                }
                throw;
            }
        }
    }

    /// <summary>Puts Explorer's taskbar back when this session had hidden it. Does nothing otherwise.</summary>
    public void Restore()
    {
        lock (_gate)
        {
            if (!_hidden)
                return;
            _taskbar?.Restore();
            _hidden = false;
        }
    }

    public void Dispose() => Restore();
}

/// <summary>
/// The <c>ExplorerTaskbar</c> singleton. The default shell sets <see cref="Hidden"/> from
/// <c>Preferences.hideExplorerTaskbar</c>. Without a session, as in a test that did not register one, setting it
/// does nothing and does not look at Explorer.
/// </summary>
public sealed class ExplorerTaskbarService : QObject
{
    private readonly QmlEngine _engine;

    internal ExplorerTaskbarService(QmlEngine engine) => _engine = engine;

    /// <summary>Whether Explorer's taskbar is hidden and its screen edge is released.</summary>
    [Qml]
    public bool Hidden
    {
        get
        {
            Track();
            return _engine.GetService<ExplorerTaskbarSession>()?.Hidden ?? false;
        }
        set
        {
            // Disposing an engine tears the binding down and would write false. A reload does that to the old
            // generation after the new one has already applied the preference, so ignore it. Process exit restores.
            if (_engine.IsDisposed)
                return;
            var session = _engine.GetService<ExplorerTaskbarSession>();
            if (session is null || session.Hidden == value)
                return;
            if (value)
                session.Hide();
            else
                session.Restore();
            NotifyChanged("hidden");
        }
    }

    internal static void Register(ModuleRegistry registry) =>
        registry.Module("Winshell").Singleton(static e => new ExplorerTaskbarService(e), "ExplorerTaskbar");
}
