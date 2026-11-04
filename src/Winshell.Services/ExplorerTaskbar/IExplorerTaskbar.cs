namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Hides Explorer's taskbar and releases the screen edge it reserved, then puts both back.
/// The only implementation that calls Win32 is <see cref="Win32ExplorerTaskbar"/>.
/// </summary>
internal interface IExplorerTaskbar
{
    /// <summary>Hides the taskbar windows. Called again, it takes any window it has not taken yet.</summary>
    void Hide();

    void Restore();
}
