using System.Collections.Concurrent;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// Where a panel sits and which edge of its screen it holds. It chooses the screen, whether to cover it or reserve an
/// edge or follow the work area, and whether a fullscreen app puts it behind. Explorer's notices about that edge come
/// here too. A placement that arrives after the panel is gone reserves nothing: an app bar with no window is one
/// nothing can remove.
/// </summary>
internal sealed class PanelEdge(nint window, IPanelDesktop desktop, Action place)
{
    internal static readonly uint Message = Win32.WM_APP + 1;
    private static readonly uint TaskbarCreatedMessage = Win32.RegisterWindowMessageW("TaskbarCreated");

    // Every window that holds an edge now, so a crash can give them back without the windows.
    private static readonly ConcurrentDictionary<nint, IPanelDesktop> Held = new();

    private readonly AppBarReservation _reservation = new();
    private bool _registered;
    private bool _placePosted;

    /// <summary>
    /// Puts the panel on its screen. <paramref name="released"/> is a placement that arrived after the panel was
    /// gone, which reserves nothing. Returns whether it moved the window.
    /// </summary>
    internal bool Place(PanelPlacement? placement, bool visible, bool released)
    {
        if (released || placement is not { } p)
            return false;
        var monitor = (p.Screen is { } screen ? desktop.Find(screen.Id) : null) ?? desktop.Primary();
        if (monitor is not { } found)
            return false;
        var scale = found.Scale;
        var edge = EdgeFor(p.Anchors);
        var size = (int)Math.Round(p.ReservedSize * scale);
        RECT area;
        if (p.CoversScreen)
        {
            Remove();
            area = found.Bounds;
        }
        else if (edge is { } side && size > 0 && visible)
        {
            area = Reserve(side, size, found.Bounds);
        }
        else
        {
            Remove();
            area = found.Area;
        }

        var rect = DesktopSpace.Anchor(area, p, scale);
        desktop.Move(rect, p.AboveWindows && !desktop.FullscreenOn(found.Id), scale);
        return true;
    }

    /// <summary>Gives the edge back. A panel that holds none changes nothing.</summary>
    internal void Remove()
    {
        if (!_registered)
            return;
        using (desktop.Explorer())
        {
            desktop.Remove(window);
        }

        _registered = false;
        Held.TryRemove(window, out _);
        _reservation.Clear();
        desktop.WorkAreaMayHaveChanged();
    }

    /// <summary>Gives every edge back. Safe from any thread, so a crash on one can.</summary>
    internal static void ReleaseAll()
    {
        var held = new List<KeyValuePair<nint, IPanelDesktop>>();
        foreach (var pair in Held)
            if (Held.TryRemove(pair.Key, out var owner))
                held.Add(new KeyValuePair<nint, IPanelDesktop>(pair.Key, owner));
        if (held.Count == 0)
            return;
        // One lowering of this process's trays covers every bar, as a crash asks for them together.
        using (held[0].Value.Explorer())
        {
            foreach (var (bar, owner) in held)
                owner.Remove(bar);
        }
    }

    /// <summary>Handles Explorer's notices to the app bar. Returns null for any other message.</summary>
    internal nint? OnMessage(uint msg, nint wParam)
    {
        if (msg == TaskbarCreatedMessage)
        {
            ExplorerRestarted();
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
                desktop.Post(() =>
                {
                    _placePosted = false;
                    place();
                });
            }
        }

        return 0;
    }

    /// <summary>Explorer restarted and forgot every app bar: drop the edge and place the panel again.</summary>
    internal void ExplorerRestarted()
    {
        Remove();
        desktop.Post(place);
        desktop.TaskbarCreated();
    }

    /// <summary>The edge a panel with these anchors reserves: the one it spans, or none for a corner or the whole screen.</summary>
    internal static Edges? EdgeFor(Edges anchors)
    {
        var horizontal = anchors.HasFlag(Edges.Left) ^ anchors.HasFlag(Edges.Right);
        var vertical = anchors.HasFlag(Edges.Top) ^ anchors.HasFlag(Edges.Bottom);
        if (vertical && !horizontal)
            return anchors.HasFlag(Edges.Top) ? Edges.Top : Edges.Bottom;
        if (horizontal && !vertical)
            return anchors.HasFlag(Edges.Left) ? Edges.Left : Edges.Right;
        return null;
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

    /// <summary>Reserves <paramref name="thickness"/> pixels along an edge of a monitor. Returns the area the panel lays out in.</summary>
    private RECT Reserve(Edges edge, int thickness, RECT monitor)
    {
        using var explorer = desktop.Explorer();
        var side = Side(edge);
        if (!_registered)
        {
            _registered = desktop.Register(window, Message);
            if (!_registered)
                // No app bar host, as when Explorer is not running: keep the window where the zone would be.
                return monitor;
            Held[window] = desktop;
        }

        var offered = desktop.Query(window, side, monitor);
        var moved = _reservation.Reserve(Strip(edge, thickness, offered), desired =>
            desktop.Reserve(window, side, desired));
        if (moved)
            desktop.WorkAreaMayHaveChanged();
        return Area(edge, _reservation.Granted, monitor);
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
}
