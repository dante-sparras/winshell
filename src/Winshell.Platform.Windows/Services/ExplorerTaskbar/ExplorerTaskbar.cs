using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.ExplorerTaskbar;

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
