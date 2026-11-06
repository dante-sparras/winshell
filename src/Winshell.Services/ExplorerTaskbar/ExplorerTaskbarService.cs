namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Hides Explorer's taskbar for a shell that draws its own. Winshell shows it again when the shell exits, however it
/// exits. While it is hidden, a taskbar window Explorer makes is hidden too.
/// </summary>
public sealed class ExplorerTaskbarService : ObservableObject, IDisposable
{
    private readonly object _gate = new();
    private IExplorerTaskbarBackend? _backend;
    private bool _hidden;

    /// <summary>
    /// <paramref name="backend"/> is the Windows adapter, or a fake in a test. Left out, the real adapter is made the
    /// first time the taskbar is touched, so leaving it shown never looks at Explorer's windows.
    /// <paramref name="whenTaskbarMayHaveChanged"/> hears that Explorer may have made a taskbar window. This module
    /// decides that a hidden taskbar is hidden again.
    /// </summary>
    internal ExplorerTaskbarService(IExplorerTaskbarBackend? backend = null,
        Action<Action>? whenTaskbarMayHaveChanged = null)
    {
        _backend = backend;
        whenTaskbarMayHaveChanged?.Invoke(Rehide);
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

    /// <summary>Whether a taskbar window of Explorer's is hidden. This does not say that this shell hid it.</summary>
    internal bool AnyHidden()
    {
        lock (_gate)
        {
            return Backend.AnyHidden();
        }
    }

    /// <summary>
    /// Shows every hidden taskbar window and reserves its edge again, for a process that did not hide it. A window
    /// that still reserves its edge keeps it.
    /// </summary>
    internal void RestoreHidden()
    {
        lock (_gate)
        {
            Backend.RestoreHidden();
        }
    }

    /// <summary>Takes a taskbar window Explorer made since <see cref="Hidden"/> was set. Does nothing unless it is hidden.</summary>
    internal void Rehide()
    {
        lock (_gate)
        {
            if (_hidden)
                Backend.Hide();
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
    }

    private void Hide()
    {
        lock (_gate)
        {
            if (_hidden)
                return;
            var taskbar = Backend;
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
                }

                throw;
            }
        }
    }

    private IExplorerTaskbarBackend Backend => _backend ??= new ExplorerTaskbarBackend();
}
