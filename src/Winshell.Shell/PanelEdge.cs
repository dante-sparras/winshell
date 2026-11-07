using System.Collections.Concurrent;
using Winshell.Native;

namespace Winshell;

/// <summary>Where and how a panel sits on its screen.</summary>
internal sealed record PanelPlacement(
    ScreenInfo? Screen,
    Edges Anchors,
    double MarginLeft,
    double MarginTop,
    double MarginRight,
    double MarginBottom,
    double Width,
    double Height,
    bool CoversScreen,
    double ReservedSize,
    bool AboveWindows);

/// <summary>
/// Where a panel sits and which edge of its screen it holds. It chooses the screen, whether to cover it or reserve an
/// edge or follow the work area, and whether a fullscreen app puts it behind. Explorer's notices about that edge come
/// here too. A placement that arrives after the panel is gone reserves nothing: an app bar with no window is one
/// nothing can remove.
/// </summary>
internal sealed class PanelEdge(IPanelDesktop desktop) : IWindowPlacer
{
    internal static readonly uint Message = Win32.WM_APP + 1;

    private static readonly ConcurrentDictionary<IPanelDesktop, byte> Held = new();

    private readonly AppBarReservation _reservation = new();
    private PanelPlacement? _placement;
    private bool _visible;
    private bool _released;
    private bool _registered;
    private bool _placePosted;

    internal void Set(PanelPlacement placement)
    {
        _placement = placement;
        Place();
    }

    /// <summary>A hidden panel holds no edge, so a panel about to show places again to take it.</summary>
    public void Show()
    {
        _visible = true;
        Place();
    }

    public void Hide()
    {
        _visible = false;
        Remove();
    }

    public void Release()
    {
        _released = true;
        Remove();
    }

    /// <summary>A panel only changes DPI on its own when its monitor's scale changes: place it again from its logical size.</summary>
    public void OnDpiChanged(RECT suggested, bool placing)
    {
        if (!placing)
            desktop.Post(Place);
    }

    /// <summary>A shown panel follows the work area, which other bars move.</summary>
    public void OnWorkAreaChanged()
    {
        if (_visible)
            Place();
    }

    /// <summary>Puts the panel on its screen: the screen, the reservation and the z-order.</summary>
    private void Place()
    {
        if (_released || _placement is not { } p)
            return;
        var monitor = (p.Screen is { } screen ? desktop.Find(screen.Id) : null) ?? desktop.Primary();
        if (monitor is not { } found)
            return;
        var scale = found.Scale;
        var edge = EdgeFor(p.Anchors);
        var size = (int)Math.Round(p.ReservedSize * scale);
        RECT area;
        if (p.CoversScreen)
        {
            Remove();
            area = found.Bounds;
        }
        else if (edge is { } side && size > 0 && _visible)
        {
            area = Reserve(side, size, found.Bounds);
        }
        else
        {
            Remove();
            area = found.Area;
        }

        var rect = Anchor(area, p, scale);
        desktop.Move(rect, p.AboveWindows && !desktop.FullscreenOn(found.Id), scale);
    }

    /// <summary>Gives the edge back. A panel that holds none changes nothing.</summary>
    private void Remove()
    {
        if (!_registered)
            return;
        using (desktop.Explorer())
        {
            desktop.Remove();
        }

        _registered = false;
        Held.TryRemove(desktop, out _);
        _reservation.Clear();
        desktop.WorkAreaMayHaveChanged();
    }

    /// <summary>Gives every edge back. Safe from any thread, so a crash on one can.</summary>
    internal static void ReleaseAll()
    {
        var held = new List<IPanelDesktop>();
        foreach (var pair in Held)
            if (Held.TryRemove(pair.Key, out _))
                held.Add(pair.Key);
        if (held.Count == 0)
            return;
        using (held[0].Explorer())
        {
            foreach (var owner in held)
                owner.Remove();
        }
    }

    /// <summary>Handles Explorer's notices to the app bar. Returns null for any other message.</summary>
    public nint? OnMessage(WindowMessage message)
    {
        var (msg, wParam) = (message.Id, message.WParam);
        if (msg == TaskbarWindows.Created)
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
                    Place();
                });
            }
        }

        return 0;
    }

    /// <summary>Explorer restarted and forgot every app bar: drop the edge and place the panel again.</summary>
    internal void ExplorerRestarted()
    {
        Remove();
        desktop.Post(Place);
    }

    /// <summary>The edge a panel with these anchors reserves: the one it spans, or none for a corner or the whole screen.</summary>
    private static Edges? EdgeFor(Edges anchors)
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
    private static RECT Strip(Edges edge, int thickness, RECT offered)
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
    private static RECT Area(Edges edge, RECT granted, RECT monitor)
    {
        return edge switch
        {
            Edges.Bottom => monitor with { Bottom = granted.Bottom, Left = granted.Left, Right = granted.Right },
            Edges.Left => monitor with { Left = granted.Left, Top = granted.Top, Bottom = granted.Bottom },
            Edges.Right => monitor with { Right = granted.Right, Top = granted.Top, Bottom = granted.Bottom },
            _ => monitor with { Top = granted.Top, Left = granted.Left, Right = granted.Right }
        };
    }

    /// <summary>Where a panel sits inside an area of its monitor, in physical pixels: anchored edges stick to the area, margins and size scale.</summary>
    private static RECT Anchor(RECT area, PanelPlacement p, double scale)
    {
        var ml = (int)Math.Round(p.MarginLeft * scale);
        var mr = (int)Math.Round(p.MarginRight * scale);
        var mt = (int)Math.Round(p.MarginTop * scale);
        var mb = (int)Math.Round(p.MarginBottom * scale);
        var w = (int)Math.Round(p.Width * scale);
        var h = (int)Math.Round(p.Height * scale);
        var (x, width) = Span(area.Left, area.Width, p.Anchors.HasFlag(Edges.Left), p.Anchors.HasFlag(Edges.Right), ml,
            mr, w);
        var (y, height) = Span(area.Top, area.Height, p.Anchors.HasFlag(Edges.Top), p.Anchors.HasFlag(Edges.Bottom), mt,
            mb, h);
        return new RECT(x, y, x + Math.Max(1, width), y + Math.Max(1, height));
    }

    private static (int Start, int Length) Span(int start, int length, bool near, bool far, int nearMargin,
        int farMargin, int size)
    {
        if (near && far)
            return (start + nearMargin, length - nearMargin - farMargin);
        if (near)
            return (start + nearMargin, size);
        if (far)
            return (start + length - size - farMargin, size);
        return (start + (length - size) / 2, size);
    }

    /// <summary>Reserves <paramref name="thickness"/> pixels along an edge of a monitor. Returns the area the panel lays out in.</summary>
    private RECT Reserve(Edges edge, int thickness, RECT monitor)
    {
        using var explorer = desktop.Explorer();
        var side = Side(edge);
        if (!_registered)
        {
            _registered = desktop.Register(Message);
            if (!_registered)
                // No app bar host, as when Explorer is not running: keep the window where the zone would be.
                return monitor;
            Held[desktop] = 0;
        }

        var offered = desktop.Query(side, monitor);
        var moved = _reservation.Reserve(Strip(edge, thickness, offered), desired =>
            desktop.Reserve(side, desired));
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
