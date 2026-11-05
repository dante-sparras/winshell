using Windows.Foundation;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// Where a native window is and whether it shows: its screen, scale, size and place, its z-order, and the parts of it
/// that take clicks. A panel reserves its edge through its <see cref="AppBar"/>, and a floating window sits on its
/// screen through its <see cref="Frame"/>.
/// </summary>
internal sealed class HostPlacement
{
    private readonly HostWindow _host;
    private readonly ShellPlatform _platform;
    private bool _visible;
    private bool _clickThrough;
    private Point _origin;
    private string _screenId = "";

    public HostPlacement(HostWindow host, ShellPlatform platform)
    {
        _host = host;
        _platform = platform;
        if (host.Kind == WindowKind.Panel)
            AppBar = new AppBar(host.Handle, platform, PlacePanel);
        else if (host.Kind == WindowKind.Floating)
            Frame = new FloatingFrame(host, platform);
    }

    /// <summary>A panel's app bar with Explorer, which reserves its edge while it is registered.</summary>
    internal AppBar? AppBar { get; }

    /// <summary>How a floating window sits on its screen.</summary>
    internal FloatingFrame? Frame { get; }

    internal PanelPlacement? Panel { get; private set; }

    internal PopupPlacement? Popup { get; private set; }

    public bool IsVisible => _visible;

    public Point Origin => _origin;

    public double Scale { get; internal set; } = 1;

    /// <summary>Whether <see cref="Move"/> is placing the window, so the messages it sends come from that placement.</summary>
    internal bool Placing { get; private set; }

    private nint Handle => _host.Handle;

    public void SetPanel(PanelPlacement placement)
    {
        Panel = placement;
        SetExStyle(Win32.WS_EX_NOACTIVATE, !placement.Focusable);
        PlacePanel();
    }

    /// <summary>Puts the panel on its screen, reserving its exclusive zone through an app bar.</summary>
    internal void PlacePanel()
    {
        // Explorer's notice that another bar moved can land on the loop after this window is gone. An app bar asked
        // for with no window is one nothing can remove, and it holds the edge after the shell has exited.
        if (_host.Released || Panel is not { } p || AppBar is not { } appBar)
            return;
        // A panel whose screen was unplugged moves to the primary screen until that screen comes back.
        var monitor = (p.Screen is { } screen ? Screens.Find(screen.Id) : null) ?? Screens.Find(_platform.PrimaryScreenId);
        if (monitor is null)
            return;
        Scale = monitor.Scale;
        var edge = AppBar.EdgeFor(p.Anchors);
        var size = (int)Math.Round(p.ReservedSize * Scale);
        RECT area;
        if (p.CoversScreen)
        {
            appBar.Remove();
            area = monitor.Bounds;
        }
        else if (edge is { } e && size > 0 && _visible)
        {
            area = appBar.Reserve(e, size, monitor.Bounds);
        }
        else
        {
            appBar.Remove();
            area = monitor.Area;
        }
        var rect = DesktopSpace.Anchor(area, p, Scale);
        var topmost = p.AboveWindows && !_platform.FullscreenOn(monitor.Info.Id);
        SetExStyle(Win32.WS_EX_TOPMOST, topmost);
        // HWND_NOTOPMOST would put it above every window that is not topmost, so over the fullscreen app.
        var after = topmost ? Win32.HWND_TOPMOST : Win32.HWND_BOTTOM;
        Move(rect, after, Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    public void SetPopup(PopupPlacement placement)
    {
        Popup = placement;
        SetExStyle(Win32.WS_EX_NOACTIVATE, !placement.GrabFocus);
        // The rect is relative to the parent window's screen, so it maps back through that screen's scale.
        var monitors = Screens.Enumerate();
        var at = DesktopSpace.MonitorAt(new Point(placement.Rect.X, placement.Rect.Y), monitors.Select(m => m.Info.Geometry).ToList());
        var monitor = monitors.FirstOrDefault(m => m.Info.Id == placement.Screen?.Id) ?? (at is { } i ? monitors[i] : monitors.FirstOrDefault());
        if (monitor is null)
            return;
        Scale = monitor.Scale;
        var r = DesktopSpace.ToPhysical(placement.Rect, monitor.Bounds, Scale);
        Move(r, Win32.HWND_TOPMOST, Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
        _platform.UpdatePopupGrab(_host, _visible && placement.GrabFocus);
    }

    public void SetFloating(FloatingPlacement placement)
    {
        if (Handle != 0 && !_host.Released)
            Frame?.Apply(placement);
    }

    /// <summary>Places a panel or popup again from its logical size, as after its monitor's scale changed.</summary>
    internal void Replace()
    {
        if (Handle == 0)
            return;
        if (_host.Kind == WindowKind.Panel)
            PlacePanel();
        else if (Popup is { } popup)
            SetPopup(popup);
    }

    public (int X, int Y) MapToScreen(double x, double y)
    {
        var pt = new POINT { X = (int)Math.Round(x * Scale), Y = (int)Math.Round(y * Scale) };
        Win32.ClientToScreen(Handle, ref pt);
        return (pt.X, pt.Y);
    }

    /// <summary>Whether a point on the virtual desktop, in physical pixels, is inside this window.</summary>
    internal bool ContainsScreenPoint(POINT pt)
    {
        Win32.GetWindowRect(Handle, out var r);
        return pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
    }

    /// <summary>Moves the window to a rectangle in physical pixels, then tells its client the size and place it got.</summary>
    internal void Move(RECT rect, nint after, uint flags)
    {
        // Moving onto a monitor of another DPI sends WM_DPICHANGED from inside SetWindowPos; this rect is already right for it.
        Placing = true;
        try
        {
            Win32.SetWindowPos(Handle, after, rect.Left, rect.Top, rect.Width, rect.Height, flags);
        }
        finally
        {
            Placing = false;
        }
        UpdateSize();
    }

    private void SetExStyle(uint flag, bool on)
    {
        var ex = (uint)Win32.GetWindowLongPtr(Handle, Win32.GWL_EXSTYLE);
        var next = on ? ex | flag : ex & ~flag;
        if (next != ex)
            Win32.SetWindowLongPtr(Handle, Win32.GWL_EXSTYLE, (nint)next);
    }

    internal void UpdateSize()
    {
        Win32.GetClientRect(Handle, out var rc);
        var origin = new POINT();
        Win32.ClientToScreen(Handle, ref origin);
        var (device, monitor) = Screens.Of(Handle);
        _origin = DesktopSpace.ToLogical(origin, monitor, Scale);
        var width = Math.Max(1, rc.Width);
        var height = Math.Max(1, rc.Height);
        _host.Presentation.FitIsland(width, height);
        _host.Client.OnResized(width / Scale, height / Scale, Scale);
        _host.Client.OnMoved(_origin.X, _origin.Y);
        // Last, since a window that follows its screen can place itself again.
        if (device != _screenId)
        {
            _screenId = device;
            _host.Client.OnScreenChanged(device);
        }
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
            return;
        _visible = visible;
        var kind = _host.Kind;
        if (visible)
        {
            if (kind == WindowKind.Panel)
                PlacePanel();
            var show = kind == WindowKind.Floating ? Win32.SW_SHOW : (kind == WindowKind.Popup && Popup?.GrabFocus == true ? Win32.SW_SHOW : Win32.SW_SHOWNOACTIVATE);
            Win32.ShowWindow(Handle, show);
            // A process's first SW_SHOW takes the state its launcher asked for, so a hidden start would hide the window.
            if (!Win32.IsWindowVisible(Handle))
                Win32.ShowWindow(Handle, show);
            if (kind == WindowKind.Floating && !Win32.IsIconic(Handle))
                OwnedWindows.Raise(Handle, _platform.Windows);
            if (kind == WindowKind.Popup && Popup?.GrabFocus == true)
                Win32.SetForegroundWindow(Handle);
        }
        else
        {
            AppBar?.Remove();
            Win32.ShowWindow(Handle, Win32.SW_HIDE);
        }
        if (kind == WindowKind.Popup)
            _platform.UpdatePopupGrab(_host, visible && Popup?.GrabFocus == true);
    }

    public void SetInputRegion(IReadOnlyList<Rect>? rects)
    {
        if (rects is null)
        {
            Win32.SetWindowRgn(Handle, 0, true);
            SetClickThrough(false);
            return;
        }
        if (rects.Count == 0)
        {
            // Nothing takes input: let every click through, but keep drawing.
            Win32.SetWindowRgn(Handle, 0, true);
            SetClickThrough(true);
            return;
        }
        SetClickThrough(false);
        var region = Win32.CreateRectRgn(0, 0, 0, 0);
        foreach (var r in rects)
        {
            var part = Win32.CreateRectRgn((int)Math.Floor(r.X * Scale), (int)Math.Floor(r.Y * Scale), (int)Math.Ceiling(r.Right * Scale), (int)Math.Ceiling(r.Bottom * Scale));
            _ = Win32.CombineRgn(region, region, part, Win32.RGN_OR);
            Win32.DeleteObject(part);
        }
        // The window owns the region once it is set.
        Win32.SetWindowRgn(Handle, region, true);
    }

    private void SetClickThrough(bool on)
    {
        if (_clickThrough == on)
            return;
        _clickThrough = on;
        SetExStyle(Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED, on);
        if (on)
            Win32.SetLayeredWindowAttributes(Handle, 0, 255, Win32.LWA_ALPHA);
    }

    public void Raise() => Win32.SetWindowPos(Handle, Win32.HWND_TOP, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

    public void StartSystemMove()
    {
        Win32.ReleaseCapture();
        Win32.SendMessageW(Handle, Win32.WM_SYSCOMMAND, (nint)(Win32.SC_MOVE | 2), 0);
    }

    public void StartSystemResize(Edges edges)
    {
        // SC_SIZE + WMSZ_* edge code.
        var code = edges switch
        {
            Edges.Left => 1,
            Edges.Right => 2,
            Edges.Top => 3,
            Edges.Top | Edges.Left => 4,
            Edges.Top | Edges.Right => 5,
            Edges.Bottom => 6,
            Edges.Bottom | Edges.Left => 7,
            Edges.Bottom | Edges.Right => 8,
            _ => 8,
        };
        Win32.ReleaseCapture();
        Win32.SendMessageW(Handle, Win32.WM_SYSCOMMAND, (nint)(Win32.SC_SIZE + code), 0);
    }

    /// <summary>Gives back what the window holds while it is placed: a popup's grab on clicks and a panel's edge.</summary>
    internal void Release()
    {
        _platform.UpdatePopupGrab(_host, false);
        AppBar?.Remove();
    }
}
