using System.Collections.Concurrent;
using Winshell.Native;

namespace Winshell.Tray;

/// <summary>
/// Every app-bar message this process sends, each with this process's trays behind Explorer's taskbar and back in
/// front after it. When the first <c>Shell_TrayWnd</c> belongs to the calling process, <c>SHAppBarMessage</c> passes
/// the rectangle by an address in that process, which Explorer cannot use once a tray passes the message on:
/// <c>ABM_QUERYPOS</c> and <c>ABM_SETPOS</c> then do nothing, and the work area stays as it was.
/// </summary>
internal static class AppBarMessages
{
    private static readonly ConcurrentDictionary<ITrayBackend, byte> Trays = new();

    /// <inheritdoc cref="AppBars.Register"/>
    internal static bool Register(nint window, uint callback)
    {
        return Send(() => AppBars.Register(window, callback));
    }

    /// <inheritdoc cref="AppBars.Query"/>
    internal static RECT Query(nint window, AppBarEdge edge, RECT proposed)
    {
        return Send(() => AppBars.Query(window, edge, proposed));
    }

    /// <inheritdoc cref="AppBars.Reserve"/>
    internal static RECT Reserve(nint window, AppBarEdge edge, RECT desired)
    {
        return Send(() => AppBars.Reserve(window, edge, desired));
    }

    /// <inheritdoc cref="AppBars.Remove"/>
    internal static bool Remove(nint window)
    {
        return Send(() => AppBars.Remove(window));
    }

    /// <inheritdoc cref="AppBars.Autohide"/>
    internal static bool Autohide(nint window)
    {
        return Send(() => AppBars.Autohide(window));
    }

    /// <inheritdoc cref="AppBars.TryPosition"/>
    internal static bool TryPosition(nint window, out AppBarEdge edge, out RECT bounds)
    {
        var (found, foundEdge, foundBounds) = Send(() =>
            (AppBars.TryPosition(window, out var e, out var b), e, b));
        edge = foundEdge;
        bounds = foundBounds;
        return found;
    }

    /// <summary>A tray of this process opened its window, which stands in front of Explorer's taskbar.</summary>
    internal static void Opened(ITrayBackend tray)
    {
        Trays[tray] = 0;
    }

    /// <summary>The tray closed its window, so messages no longer step around it.</summary>
    internal static void Closed(ITrayBackend tray)
    {
        Trays.TryRemove(tray, out _);
    }

    private static T Send<T>(Func<T> message)
    {
        List<ITrayBackend> lowered = [.. Trays.Keys.Where(tray => tray.Lower())];
        try
        {
            return message();
        }
        finally
        {
            foreach (var tray in lowered)
                if (Trays.ContainsKey(tray))
                    tray.Raise();
        }
    }
}
