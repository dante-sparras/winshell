namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Hides Explorer's taskbar for a shell that draws its own. The session belongs to the program, which restores the
/// taskbar when it exits, however it exits.
/// </summary>
public sealed class ExplorerTaskbarService : ObservableObject
{
    private readonly ExplorerTaskbarSession _session;

    internal ExplorerTaskbarService(ExplorerTaskbarSession session) => _session = session;

    /// <summary>Whether Explorer's taskbar is hidden and its screen edge is released.</summary>
    public bool Hidden
    {
        get => _session.Hidden;
        set
        {
            if (_session.Hidden == value)
                return;
            if (value)
                _session.Hide();
            else
                _session.Restore();
            Changed();
        }
    }
}
