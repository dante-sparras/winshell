using Winshell.Native;

namespace Winshell;

/// <summary>A monitor as <see cref="PanelEdge"/> places on it, in physical pixels.</summary>
/// <param name="Id">The screen id, as <see cref="ScreenInfo.Id"/>.</param>
/// <param name="Bounds">The whole monitor.</param>
/// <param name="Area">The work area, or the whole monitor when it has none.</param>
/// <param name="Scale">Physical pixels per logical pixel.</param>
internal readonly record struct PanelMonitor(string Id, RECT Bounds, RECT Area, double Scale);

/// <summary>
/// What <see cref="PanelEdge"/> asks of the desktop: which screens exist, Explorer's app bar, and where the window
/// sits. The shell has a Win32 adapter. Tests use a fake, so they never open a window.
/// </summary>
internal interface IPanelDesktop
{
    /// <summary>The monitor with this id, or null when it is unplugged.</summary>
    PanelMonitor? Find(string id);

    /// <summary>The primary monitor, or null when none is connected.</summary>
    PanelMonitor? Primary();

    /// <summary>Whether another app covers this screen, so a panel there steps behind it.</summary>
    bool FullscreenOn(string screenId);

    /// <summary>
    /// Puts Explorer's taskbar in front of this process's trays until disposed, so the app-bar calls inside it reach
    /// Explorer.
    /// </summary>
    IDisposable Explorer();

    /// <summary>Registers <paramref name="window"/> so Explorer posts <paramref name="callback"/> to it. False when no host answers.</summary>
    bool Register(nint window, uint callback);

    /// <summary>The part of <paramref name="edge"/> still free inside <paramref name="proposed"/>.</summary>
    RECT Query(nint window, AppBarEdge edge, RECT proposed);

    /// <summary>Reserves <paramref name="desired"/> on <paramref name="edge"/> and returns the rectangle Explorer granted.</summary>
    RECT Reserve(nint window, AppBarEdge edge, RECT desired);

    /// <summary>Removes <paramref name="window"/>'s app bar.</summary>
    void Remove(nint window);

    /// <summary>
    /// Moves the window to <paramref name="rect"/> at <paramref name="scale"/>. The scale is applied before the window
    /// moves, because the move reports the size, and a DPI change during it keeps the scale it started with.
    /// <paramref name="topmost"/> false puts it behind other windows, as when a fullscreen app covers its screen.
    /// </summary>
    void Move(RECT rect, bool topmost, double scale);

    /// <summary>The work area may have moved, so panels that follow other bars place again.</summary>
    void WorkAreaMayHaveChanged();

    /// <summary>Runs <paramref name="action"/> on the shell's thread.</summary>
    void Post(Action action);
}
