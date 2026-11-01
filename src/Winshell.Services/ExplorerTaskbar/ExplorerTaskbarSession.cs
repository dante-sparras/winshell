namespace Winshell.ExplorerTaskbar;

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
