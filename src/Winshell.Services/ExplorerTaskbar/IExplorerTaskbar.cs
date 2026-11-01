namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Hides Explorer's taskbar and releases the screen edge it reserved, then puts both back.
/// The only implementation that calls Win32 is <see cref="Win32ExplorerTaskbar"/>.
/// </summary>
internal interface IExplorerTaskbar
{
    void Hide();

    void Restore();
}
