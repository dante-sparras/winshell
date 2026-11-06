using System.Collections.Concurrent;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// A panel's app bar with Explorer, which reserves an edge of its screen so maximized windows stay out of the zone.
/// When Explorer says the zones moved, <c>place</c> places the panel again.
/// </summary>
internal sealed class AppBar(nint window, ShellPlatform platform, Action place)
{
    internal static readonly uint Message = Win32.WM_APP + 1;
    private static readonly uint TaskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");

    // Every window that holds an app bar now, for RemoveAll when a crash skips Remove.
    private static readonly ConcurrentDictionary<nint, byte> Held = new();

    private readonly AppBarReservation _reservation = new();
    private bool _placePosted;

    /// <summary>Whether Explorer has the app bar, and with it the edge.</summary>
    public bool Registered { get; private set; }

    /// <summary>The edge a panel with these anchors reserves: the one it spans, or none for a corner or the whole screen.</summary>
    public static Edges? EdgeFor(Edges anchors)
    {
        var h = anchors.HasFlag(Edges.Left) ^ anchors.HasFlag(Edges.Right);
        var v = anchors.HasFlag(Edges.Top) ^ anchors.HasFlag(Edges.Bottom);
        if (v && !h)
            return anchors.HasFlag(Edges.Top) ? Edges.Top : Edges.Bottom;
        if (h && !v)
            return anchors.HasFlag(Edges.Left) ? Edges.Left : Edges.Right;
        return null;
    }

    /// <summary>Reserves <paramref name="thickness"/> pixels along an edge of a monitor. Returns the area the panel lays out in.</summary>
    public RECT Reserve(Edges edge, int thickness, RECT monitor)
    {
        using var explorerFirst = ShellPlatform.ExplorerFirst();
        var side = Side(edge);
        if (!Registered)
        {
            Registered = AppBars.Register(window, Message);
            if (!Registered)
                // No app bar host, as when Explorer is not running: keep the window where the zone would be.
                return monitor;
            Held[window] = 0;
        }

        var offered = AppBars.Query(window, side, monitor);
        var moved = _reservation.Reserve(Strip(edge, thickness, offered), desired =>
            AppBars.Reserve(window, side, desired));
        // Explorer moves the work area a moment later; place the panels again then.
        if (moved)
            platform.WorkAreaMayHaveChanged();
        return Area(edge, _reservation.Granted, monitor);
    }

    public void Remove()
    {
        if (!Registered)
            return;
        using (ShellPlatform.ExplorerFirst())
        {
            AppBars.Remove(window);
        }

        Registered = false;
        Held.TryRemove(window, out _);
        _reservation.Clear();
        platform.WorkAreaMayHaveChanged();
    }

    /// <summary>Removes every app bar now held. Safe from any thread, so a crash on one can give the edges back.</summary>
    public static void RemoveAll()
    {
        // As in Remove: Explorer's taskbar first, so the request reaches Explorer rather than this process's tray host.
        using (ShellPlatform.ExplorerFirst())
        {
            foreach (var window in Held.Keys)
                AppBars.Remove(window);
        }

        Held.Clear();
    }

    /// <summary>Handles Explorer's notices to the app bar. Returns null for any other message.</summary>
    public nint? OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == TaskbarCreated)
        {
            // Explorer restarted and forgot every app bar: reserve the edge again.
            Remove();
            platform.Loop.Post(place);
            platform.TaskbarCreated();
            return 0;
        }

        if (msg != Message)
            return null;
        if (AppBars.PositionChanged(wParam))
        {
            // A burst of these places the panel once. Explorer does not send one notice per own request,
            // so each may be another bar. A short grant is asked for once more, and that same rectangle
            // sends no notice.
            _reservation.NoteExternalChange();
            if (!_placePosted)
            {
                _placePosted = true;
                platform.Loop.Post(() =>
                {
                    _placePosted = false;
                    place();
                });
            }
        }

        return 0;
    }

    private static AppBarEdge Side(Edges edge)
    {
        return edge switch
        {
            Edges.Left => AppBarEdge.Left,
            Edges.Right => AppBarEdge.Right,
            Edges.Bottom => AppBarEdge.Bottom,
            _ => AppBarEdge.Top
        };
    }

    /// <summary>The strip of <paramref name="offered"/> that is <paramref name="thickness"/> pixels deep along an edge.</summary>
    internal static RECT Strip(Edges edge, int thickness, RECT offered)
    {
        return edge switch
        {
            Edges.Bottom => offered with { Top = offered.Bottom - thickness },
            Edges.Left => offered with { Right = offered.Left + thickness },
            Edges.Right => offered with { Left = offered.Right - thickness },
            _ => offered with { Bottom = offered.Top + thickness }
        };
    }

    /// <summary>The monitor, starting at the edge the app bar got and as long as its grant along that edge.</summary>
    internal static RECT Area(Edges edge, RECT granted, RECT monitor)
    {
        return edge switch
        {
            Edges.Bottom => monitor with { Bottom = granted.Bottom, Left = granted.Left, Right = granted.Right },
            Edges.Left => monitor with { Left = granted.Left, Top = granted.Top, Bottom = granted.Bottom },
            Edges.Right => monitor with { Right = granted.Right, Top = granted.Top, Bottom = granted.Bottom },
            _ => monitor with { Top = granted.Top, Left = granted.Left, Right = granted.Right }
        };
    }
}
