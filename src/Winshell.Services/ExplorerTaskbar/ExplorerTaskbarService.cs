namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Hides Explorer's taskbar for a shell that draws its own. Winshell shows it again when the shell exits, however it
/// exits. While it is hidden, a taskbar window Explorer makes is hidden too.
/// </summary>
public sealed class ExplorerTaskbarService : ObservableObject, IDisposable
{
    private readonly object _gate = new();
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private IExplorerTaskbarBackend? _backend;
    private bool _hidden;

    /// <summary>
    /// <paramref name="backend"/> is the Windows adapter, or a fake in a test. Left out, the real adapter is made the
    /// first time the taskbar is hidden, so leaving it shown never looks at Explorer's windows.
    /// </summary>
    internal ExplorerTaskbarService(IEventLoop loop, Log log, IExplorerTaskbarBackend? backend = null)
    {
        _loop = loop;
        _log = log;
        if (backend is not null)
            _backend = Listen(backend);
    }

    /// <summary>Whether Explorer's taskbar is hidden and its screen edge is released.</summary>
    public bool Hidden
    {
        get
        {
            lock (_gate)
            {
                return _hidden;
            }
        }
        set
        {
            if (Hidden == value)
                return;
            if (value)
                Hide();
            else
                Restore();
            Changed();
        }
    }

    /// <summary>
    /// Puts back what this shell hid. Does nothing otherwise. Safe from any thread, and it reports no change: a crash
    /// can call it, and the process is already ending.
    /// </summary>
    internal void Restore()
    {
        lock (_gate)
        {
            if (!_hidden)
                return;
            _backend?.Restore();
            _hidden = false;
        }
    }

    /// <summary>Shows Explorer's taskbar again when this shell had hidden it.</summary>
    public void Dispose()
    {
        Restore();
        lock (_gate)
        {
            _backend?.Dispose();
        }
    }

    private void Hide()
    {
        lock (_gate)
        {
            if (_hidden)
                return;
            var taskbar = _backend ??= Listen(new ExplorerTaskbarBackend(_loop, _log));
            _hidden = true;
            try
            {
                taskbar.Hide();
            }
            catch
            {
                try
                {
                    taskbar.Restore();
                    _hidden = false;
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // The hide's own failure is the one thrown. Exit tries the restore again.
                    _log.Warn("explorer-taskbar",
                        $"Could not put the taskbar back after hiding it failed: {e.Message}");
                }

                throw;
            }
        }
    }

    /// <summary>Takes a taskbar window Explorer made since the taskbar was hidden. Does nothing unless it is hidden.</summary>
    private void Rehide()
    {
        lock (_gate)
        {
            if (_hidden)
                _backend?.Hide();
        }
    }

    private IExplorerTaskbarBackend Listen(IExplorerTaskbarBackend backend)
    {
        backend.TaskbarMayHaveChanged += Rehide;
        return backend;
    }
}
