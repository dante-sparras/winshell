using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Accessibility;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows;

/// <summary>The native window behind a shell window.</summary>
internal sealed unsafe class Win32Window : IWindowBackend, IRebindable
{
    private const string ClassName = "WinshellWindow";
    private static bool _registered;
    private static readonly uint AppBarMessage = Win32.WM_APP + 1;
    private static readonly uint TaskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");

    private readonly WindowsPlatform _platform;
    private readonly WindowKind _kind;
    private GCHandle _self;
    private IWindowClient _client;
    private CompositionSurface? _surface;
    private WindowAutomation _automation;
    private double _scale = 1;
    private int _physicalWidth = 1;
    private int _physicalHeight = 1;
    private bool _visible;
    private bool _trackingLeave;
    private nint _cursor;
    private PanelPlacement? _panel;
    private PopupPlacement? _popup;
    private FloatingPlacement? _floating;
    private bool _floatingPlaced;
    private bool _appBarRegistered;
    private readonly AppBarReservation _appBar = new();
    private readonly AppBarPosNotices _posNotices = new();
    private bool _placePosted;
    private uint _appBarEdge;
    private bool _fullscreenAppActive;
    private bool _frameScheduled;
    private TimeSpan _lastFrame;
    private int _buttons;
    private bool _clickThrough;
    private bool _focusable;
    private QPoint _origin;
    private string _screenId = "";
    private bool _placing;

    public Win32Window(WindowsPlatform platform, WindowKind kind, IWindowClient client)
    {
        _platform = platform;
        _kind = kind;
        _client = client;
        Register();
        var (style, exStyle) = Styles();
        Handle = Win32.CreateWindowExW(exStyle, ClassName, "", style, 0, 0, 1, 1, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (Handle == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        // Messages reach OnMessage once the window points back at this object.
        _automation = new WindowAutomation(this, client.Scene, platform.Loop, platform.Log);
        _self = GCHandle.Alloc(this);
        Win32.SetWindowLongPtr(Handle, Win32.GWLP_USERDATA, GCHandle.ToIntPtr(_self));
        _scale = Win32.GetDpiForWindow(Handle) / 96.0;
        // The DWM backdrop shows wherever the content is transparent.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Win32.DwmExtendFrameIntoClientArea(Handle, margins);
        if (kind != WindowKind.Floating)
        {
            // Panels and popups appear without the Windows fade, as layer surfaces do.
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_TRANSITIONS_FORCEDISABLED, 1, 4);
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_EXCLUDED_FROM_PEEK, 1, 4);
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, 1, 4);
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_BORDER_COLOR, Win32.DWMWA_COLOR_NONE, 4);
        }
        _surface = new CompositionSurface(Handle);
        _cursor = Win32.LoadCursorW(0, Win32.IDC_ARROW);
    }

    public nint Handle { get; private set; }

    public WindowKind Kind => _kind;

    public bool IsVisible => _visible;

    public QPoint Origin => _origin;

    public double Scale => _scale;

    public void Rebind(IWindowClient client)
    {
        _client = client;
        // A reload brings a new scene: its items are a new tree.
        _automation.Dispose();
        _automation = new WindowAutomation(this, client.Scene, _platform.Loop, _platform.Log);
        RequestFrame();
    }

    private (uint Style, uint ExStyle) Styles() => _kind switch
    {
        WindowKind.Floating => (Win32.WS_OVERLAPPEDWINDOW | Win32.WS_CLIPCHILDREN, Win32.WS_EX_NOREDIRECTIONBITMAP | Win32.WS_EX_APPWINDOW),
        WindowKind.Popup => (Win32.WS_POPUP, Win32.WS_EX_NOREDIRECTIONBITMAP | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST | Win32.WS_EX_NOACTIVATE),
        _ => (Win32.WS_POPUP, Win32.WS_EX_NOREDIRECTIONBITMAP | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST | Win32.WS_EX_NOACTIVATE),
    };

    private static void Register()
    {
        if (_registered)
            return;
        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = Win32.CS_DBLCLKS,
                lpfnWndProc = &WndProc,
                hInstance = Win32.GetModuleHandleW(null),
                hCursor = Win32.LoadCursorW(0, Win32.IDC_ARROW),
                lpszClassName = name,
            };
            Win32.RegisterClassExW(wc);
        }
        _registered = true;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var data = Win32.GetWindowLongPtr(hwnd, Win32.GWLP_USERDATA);
        if (data != 0 && GCHandle.FromIntPtr(data).Target is Win32Window window)
        {
            try
            {
                if (window.OnMessage(msg, wParam, lParam) is { } result)
                    return result;
            }
            catch (Exception e)
            {
                window._platform.Log.Error("window", e.ToString());
            }
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Placement

    public void SetPanel(PanelPlacement placement)
    {
        _panel = placement;
        _focusable = placement.Focusable;
        SetExStyle(Win32.WS_EX_NOACTIVATE, !placement.Focusable);
        PlacePanel();
    }

    /// <summary>Puts the panel on its screen, reserving its exclusive zone through an app bar.</summary>
    internal void PlacePanel()
    {
        if (_panel is not { } p)
            return;
        // A panel whose screen was unplugged moves to the primary screen until that screen comes back.
        var monitor = (p.Screen is { } screen ? Screens.Find(screen.Id) : null) ?? Screens.Find(_platform.PrimaryScreenId);
        if (monitor is null)
            return;
        _scale = monitor.Scale;
        var edge = ExclusionEdge(p.Anchors);
        var zone = (int)Math.Round(p.ExclusiveZone * _scale);
        RECT area;
        if (p.ExclusionMode == 1)
        {
            // Ignore: the whole monitor, over the taskbar and other bars.
            RemoveAppBar();
            area = monitor.Bounds;
        }
        else if (edge is { } e && zone > 0 && _visible)
        {
            area = ReserveAppBar(e, zone, monitor.Bounds);
        }
        else
        {
            RemoveAppBar();
            area = monitor.Area;
        }
        var rect = DesktopSpace.Anchor(area, p, _scale);
        var topmost = p.AboveWindows && !_fullscreenAppActive;
        SetExStyle(Win32.WS_EX_TOPMOST, topmost);
        var after = p.AboveWindows ? (topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST) : Win32.HWND_BOTTOM;
        Move(rect.Left, rect.Top, rect.Width, rect.Height, after);
    }

    private static Edges? ExclusionEdge(Edges anchors)
    {
        var h = anchors.HasFlag(Edges.Left) ^ anchors.HasFlag(Edges.Right);
        var v = anchors.HasFlag(Edges.Top) ^ anchors.HasFlag(Edges.Bottom);
        if (v && !h)
            return anchors.HasFlag(Edges.Top) ? Edges.Top : Edges.Bottom;
        if (h && !v)
            return anchors.HasFlag(Edges.Left) ? Edges.Left : Edges.Right;
        return null;
    }

    /// <summary>Registers an app bar on an edge, so maximized windows stay out of the zone. Returns the area it got.</summary>
    private RECT ReserveAppBar(Edges edge, int thickness, RECT monitor)
    {
        using var explorerFirst = Services.Tray.TrayHost.ExplorerFirst();
        var abd = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = Handle, uCallbackMessage = AppBarMessage };
        if (!_appBarRegistered)
        {
            _appBarRegistered = Win32.SHAppBarMessage(Win32.ABM_NEW, ref abd) != 0;
            if (!_appBarRegistered)
            {
                // No app bar host, as when Explorer is not running: keep the window where the zone would be.
                return monitor;
            }
        }
        _appBarEdge = edge switch
        {
            Edges.Left => Win32.ABE_LEFT,
            Edges.Right => Win32.ABE_RIGHT,
            Edges.Bottom => Win32.ABE_BOTTOM,
            _ => Win32.ABE_TOP,
        };
        abd.uEdge = _appBarEdge;
        abd.rc = monitor;
        Win32.SHAppBarMessage(Win32.ABM_QUERYPOS, ref abd);
        switch (_appBarEdge)
        {
            case Win32.ABE_TOP:
                abd.rc.Bottom = abd.rc.Top + thickness;
                break;
            case Win32.ABE_BOTTOM:
                abd.rc.Top = abd.rc.Bottom - thickness;
                break;
            case Win32.ABE_LEFT:
                abd.rc.Right = abd.rc.Left + thickness;
                break;
            default:
                abd.rc.Left = abd.rc.Right - thickness;
                break;
        }
        var previous = _appBar.Granted;
        var moved = _appBar.Reserve(abd.rc, desired =>
        {
            abd.rc = desired;
            // Counted before the call: Explorer can send ABN_POSCHANGED before ABM_SETPOS returns.
            _posNotices.ExpectOwn();
            Win32.SHAppBarMessage(Win32.ABM_SETPOS, ref abd);
            _posNotices.SetPosReturned(!abd.rc.Equals(previous));
            return abd.rc;
        });
        abd.rc = _appBar.Granted;
        // The panel lays out within the monitor, starting at the edge the app bar got.
        var rect = monitor;
        switch (_appBarEdge)
        {
            case Win32.ABE_TOP:
                rect.Top = abd.rc.Top;
                rect.Left = abd.rc.Left;
                rect.Right = abd.rc.Right;
                break;
            case Win32.ABE_BOTTOM:
                rect.Bottom = abd.rc.Bottom;
                rect.Left = abd.rc.Left;
                rect.Right = abd.rc.Right;
                break;
            case Win32.ABE_LEFT:
                rect.Left = abd.rc.Left;
                rect.Top = abd.rc.Top;
                rect.Bottom = abd.rc.Bottom;
                break;
            default:
                rect.Right = abd.rc.Right;
                rect.Top = abd.rc.Top;
                rect.Bottom = abd.rc.Bottom;
                break;
        }
        // Explorer moves the work area a moment later; place the panels again then.
        if (moved)
            _platform.WorkAreaMayHaveChanged();
        return rect;
    }

    private void RemoveAppBar()
    {
        if (!_appBarRegistered)
            return;
        var abd = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = Handle };
        using (Services.Tray.TrayHost.ExplorerFirst())
            Win32.SHAppBarMessage(Win32.ABM_REMOVE, ref abd);
        _appBarRegistered = false;
        _appBar.Clear();
        _posNotices.Reset();
        _platform.WorkAreaMayHaveChanged();
    }

    public void SetPopup(PopupPlacement placement)
    {
        _popup = placement;
        SetExStyle(Win32.WS_EX_NOACTIVATE, !placement.GrabFocus);
        // The rect is relative to the parent window's screen, so it maps back through that screen's scale.
        var monitors = Screens.Enumerate();
        var at = DesktopSpace.MonitorAt(new QPoint(placement.Rect.X, placement.Rect.Y), monitors.Select(m => m.Info.Geometry).ToList());
        var monitor = monitors.FirstOrDefault(m => m.Info.Id == placement.Screen?.Id) ?? (at is { } i ? monitors[i] : monitors.FirstOrDefault());
        if (monitor is null)
            return;
        _scale = monitor.Scale;
        var r = DesktopSpace.ToPhysical(placement.Rect, monitor.Bounds, _scale);
        Move(r.Left, r.Top, r.Width, r.Height, Win32.HWND_TOPMOST);
        _platform.UpdatePopupGrab(this, _visible && placement.GrabFocus);
    }

    public void SetFloating(FloatingPlacement placement)
    {
        var first = !_floatingPlaced;
        _floating = placement;
        Win32.SetWindowTextW(Handle, placement.Title);
        if (first || placement.Width != _lastFloatingWidth || placement.Height != _lastFloatingHeight)
        {
            _lastFloatingWidth = placement.Width;
            _lastFloatingHeight = placement.Height;
            var dpi = Win32.GetDpiForWindow(Handle);
            _scale = dpi / 96.0;
            var client = new RECT(0, 0, (int)Math.Round(placement.Width * _scale), (int)Math.Round(placement.Height * _scale));
            var (style, ex) = Styles();
            Win32.AdjustWindowRectExForDpi(ref client, style, false, ex, dpi);
            int x, y;
            if (first)
            {
                var area = Screens.Find(placement.Screen?.Id ?? _platform.PrimaryScreenId)?.Area ?? default;
                x = area.Left + (area.Width - client.Width) / 2;
                y = area.Top + (area.Height - client.Height) / 2;
            }
            else
            {
                Win32.GetWindowRect(Handle, out var current);
                x = current.Left;
                y = current.Top;
            }
            Win32.SetWindowPos(Handle, 0, x, y, client.Width, client.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            _floatingPlaced = true;
        }
        if (_visible)
        {
            if (placement.Minimized && !Win32.IsIconic(Handle))
                Win32.ShowWindow(Handle, Win32.SW_MINIMIZE);
            else if (placement.Maximized && !Win32.IsZoomed(Handle))
                Win32.ShowWindow(Handle, Win32.SW_SHOWMAXIMIZED);
            else if (!placement.Maximized && !placement.Minimized && (Win32.IsZoomed(Handle) || Win32.IsIconic(Handle)))
                Win32.ShowWindow(Handle, Win32.SW_RESTORE);
        }
    }

    private double _lastFloatingWidth = -1, _lastFloatingHeight = -1;

    private void Replace()
    {
        if (Handle == 0)
            return;
        if (_kind == WindowKind.Panel)
            PlacePanel();
        else if (_popup is { } popup)
            SetPopup(popup);
    }

    public (int X, int Y) MapToScreen(double x, double y)
    {
        var pt = new POINT { X = (int)Math.Round(x * _scale), Y = (int)Math.Round(y * _scale) };
        Win32.ClientToScreen(Handle, ref pt);
        return (pt.X, pt.Y);
    }

    private void Move(int x, int y, int w, int h, nint after)
    {
        // Moving onto a monitor of another DPI sends WM_DPICHANGED from inside SetWindowPos; this rect is already right for it.
        _placing = true;
        try
        {
            Win32.SetWindowPos(Handle, after, x, y, w, h, Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
        }
        finally
        {
            _placing = false;
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

    private void UpdateSize()
    {
        Win32.GetClientRect(Handle, out var rc);
        var origin = new POINT();
        Win32.ClientToScreen(Handle, ref origin);
        var (device, monitor) = Screens.Of(Handle);
        _origin = DesktopSpace.ToLogical(origin, monitor, _scale);
        var changed = rc.Width != _physicalWidth || rc.Height != _physicalHeight;
        _physicalWidth = Math.Max(1, rc.Width);
        _physicalHeight = Math.Max(1, rc.Height);
        _client.OnResized(_physicalWidth / _scale, _physicalHeight / _scale, _scale);
        _client.OnMoved(_origin.X, _origin.Y);
        if (changed)
            RenderNow();
        // Last, since a binding on the window's screen can place the window again.
        if (device != _screenId)
        {
            _screenId = device;
            _client.OnScreenChanged(device);
        }
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
            return;
        _visible = visible;
        if (visible)
        {
            if (_kind == WindowKind.Panel)
                PlacePanel();
            RenderNow();
            var show = _kind == WindowKind.Floating ? Win32.SW_SHOW : (_kind == WindowKind.Popup && _popup?.GrabFocus == true ? Win32.SW_SHOW : Win32.SW_SHOWNOACTIVATE);
            Win32.ShowWindow(Handle, show);
            if (_kind == WindowKind.Popup && _popup?.GrabFocus == true)
                Win32.SetForegroundWindow(Handle);
        }
        else
        {
            RemoveAppBar();
            Win32.ShowWindow(Handle, Win32.SW_HIDE);
        }
        if (_kind == WindowKind.Popup)
            _platform.UpdatePopupGrab(this, visible && _popup?.GrabFocus == true);
    }

    public void SetInputRegion(IReadOnlyList<QRect>? rects)
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
            var part = Win32.CreateRectRgn((int)Math.Floor(r.X * _scale), (int)Math.Floor(r.Y * _scale), (int)Math.Ceiling(r.Right * _scale), (int)Math.Ceiling(r.Bottom * _scale));
            Win32.CombineRgn(region, region, part, Win32.RGN_OR);
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
            SetLayeredWindowAttributes(Handle, 0, 255, 2);
    }

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    public void SetCursor(int shape)
    {
        var id = shape switch
        {
            1 => Win32.IDC_UPARROW,
            2 => Win32.IDC_CROSS,
            3 => Win32.IDC_WAIT,
            4 => Win32.IDC_IBEAM,
            5 or 11 => Win32.IDC_SIZENS,
            6 or 12 => Win32.IDC_SIZEWE,
            7 => Win32.IDC_SIZENESW,
            8 => Win32.IDC_SIZENWSE,
            9 or 18 => Win32.IDC_SIZEALL,
            13 or 17 => Win32.IDC_HAND,
            14 => Win32.IDC_NO,
            15 => Win32.IDC_HELP,
            16 => Win32.IDC_APPSTARTING,
            _ => Win32.IDC_ARROW,
        };
        _cursor = shape == 10 ? 0 : Win32.LoadCursorW(0, id);
        Win32.GetCursorPos(out var pt);
        if (Win32.WindowFromPoint(pt) == Handle)
            Win32.SetCursor(_cursor);
    }

    public void SetBackdrop(WindowBackdrop backdrop, WindowCorners corners, bool darkFrame)
    {
        var type = backdrop switch
        {
            WindowBackdrop.Mica => 2,
            WindowBackdrop.Acrylic => 3,
            WindowBackdrop.MicaAlt => 4,
            _ => 1,
        };
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, darkFrame ? 1 : 0, 4);
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_SYSTEMBACKDROP_TYPE, type, 4);
        var corner = corners switch
        {
            WindowCorners.Square => 1,
            WindowCorners.Round => 2,
            WindowCorners.RoundSmall => 3,
            // A bar along an edge stays square; a popup with a material is a rounded flyout.
            _ => _kind switch
            {
                WindowKind.Floating => 0,
                WindowKind.Popup when backdrop != WindowBackdrop.None => 2,
                _ => 1,
            },
        };
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, corner, 4);
        if (_kind != WindowKind.Floating)
        {
            // A material window gets Windows' hairline border; a plain one gets none.
            var border = _kind == WindowKind.Popup && backdrop != WindowBackdrop.None ? 0xFFFFFFFF : Win32.DWMWA_COLOR_NONE;
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_BORDER_COLOR, border, 4);
        }
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

    public void SetKeyboardFocus(bool focus)
    {
        if (!focus)
            return;
        if (_kind == WindowKind.Panel && !_focusable)
            return;
        Win32.SetForegroundWindow(Handle);
        Win32.SetFocus(Handle);
    }

    // Frames

    public void RequestFrame()
    {
        if (_frameScheduled || Handle == 0)
            return;
        _frameScheduled = true;
        var loop = _platform.Loop;
        var wait = AnimationClock.FrameInterval - (loop.Now - _lastFrame);
        if (wait <= TimeSpan.Zero)
            loop.Post(Frame);
        else
            loop.Schedule(wait, Frame);
    }

    private void Frame()
    {
        _frameScheduled = false;
        RenderNow();
    }

    private void RenderNow()
    {
        if (_surface is null || Handle == 0)
            return;
        _lastFrame = _platform.Loop.Now;
        try
        {
            _surface.Render(_physicalWidth, _physicalHeight, canvas => _client.OnPaint(canvas));
        }
        catch (SharpGen.Runtime.SharpGenException e)
        {
            _platform.Log.Error("render", $"Could not draw a frame: {e.Message}");
        }
        _automation.AfterFrame();
    }

    // Messages

    private nint? OnMessage(uint msg, nint wParam, nint lParam)
    {
        var scene = _client.Scene;
        switch (msg)
        {
            case Win32.WM_SIZE:
            case Win32.WM_MOVE:
                if (Handle != 0)
                    UpdateSize();
                if (_kind == WindowKind.Floating)
                    _client.OnStateChanged(Win32.IsZoomed(Handle), Win32.IsIconic(Handle), false);
                return null;
            case Win32.WM_DPICHANGED:
                {
                    _scale = DesktopSpace.Scale((uint)Win32.HighWord(wParam));
                    var suggested = *(RECT*)lParam;
                    if (_kind == WindowKind.Floating)
                    {
                        // The user dragged it to another monitor, or that monitor's scale changed: Windows' rect keeps the logical size.
                        Win32.SetWindowPos(Handle, 0, suggested.Left, suggested.Top, suggested.Width, suggested.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                    }
                    else if (!_placing)
                    {
                        // A panel or popup only changes DPI on its own when its monitor's scale changes: place it again from its logical size.
                        _platform.Loop.Post(Replace);
                    }
                    UpdateSize();
                    _platform.ScreensMayHaveChanged();
                    return 0;
                }
            case Win32.WM_MOUSEACTIVATE:
                if (_kind == WindowKind.Panel && !_focusable)
                    return Win32.MA_NOACTIVATE;
                if (_kind == WindowKind.Popup && _popup?.GrabFocus != true)
                    return Win32.MA_NOACTIVATE;
                return null;
            case Win32.WM_SETCURSOR:
                if (Win32.LowWord(lParam) == Win32.HTCLIENT)
                {
                    Win32.SetCursor(_cursor);
                    return 1;
                }
                return null;
            case Win32.WM_MOUSEMOVE:
                {
                    if (!_trackingLeave)
                    {
                        var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = Win32.TME_LEAVE, hwndTrack = Handle };
                        Win32.TrackMouseEvent(ref tme);
                        _trackingLeave = true;
                    }
                    var (x, y) = Point(lParam);
                    scene.PointerMove(x, y, Keys.Modifiers());
                    return 0;
                }
            case Win32.WM_MOUSELEAVE:
                _trackingLeave = false;
                if (_buttons == 0)
                    scene.PointerLeave();
                return 0;
            case Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or Win32.WM_XBUTTONDOWN
                or Win32.WM_LBUTTONDBLCLK or Win32.WM_RBUTTONDBLCLK or Win32.WM_MBUTTONDBLCLK or Win32.WM_XBUTTONDBLCLK:
                {
                    var button = Button(msg, wParam);
                    _buttons |= button;
                    Win32.SetCapture(Handle);
                    var (x, y) = Point(lParam);
                    var dbl = msg is Win32.WM_LBUTTONDBLCLK or Win32.WM_RBUTTONDBLCLK or Win32.WM_MBUTTONDBLCLK or Win32.WM_XBUTTONDBLCLK;
                    scene.PointerPress(x, y, button, Keys.Modifiers(), dbl);
                    return 0;
                }
            case Win32.WM_LBUTTONUP or Win32.WM_RBUTTONUP or Win32.WM_MBUTTONUP or Win32.WM_XBUTTONUP:
                {
                    var button = Button(msg, wParam);
                    _buttons &= ~button;
                    if (_buttons == 0)
                        Win32.ReleaseCapture();
                    var (x, y) = Point(lParam);
                    scene.PointerRelease(x, y, button, Keys.Modifiers());
                    return 0;
                }
            case Win32.WM_CAPTURECHANGED:
                if (_buttons != 0 && lParam != Handle)
                {
                    _buttons = 0;
                    scene.PointerCancel();
                }
                return 0;
            case Win32.WM_MOUSEWHEEL or Win32.WM_MOUSEHWHEEL:
                {
                    var pt = new POINT { X = Win32.LowWord(lParam), Y = Win32.HighWord(lParam) };
                    Win32.ScreenToClient(Handle, ref pt);
                    var delta = Win32.HighWord(wParam);
                    var vertical = msg == Win32.WM_MOUSEWHEEL;
                    scene.Wheel(pt.X / _scale, pt.Y / _scale, vertical ? 0 : -delta, vertical ? delta : 0, Keys.Modifiers());
                    return 0;
                }
            case Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN:
                {
                    _ime.ClearPending();
                    var e = new KeyEvent { Key = Keys.ToQt((int)wParam), Modifiers = Keys.Modifiers(), IsAutoRepeat = ((long)lParam & (1L << 30)) != 0, NativeScanCode = (int)(((long)lParam >> 16) & 0xff) };
                    if (scene.KeyPress(e))
                        return 0;
                    if (e.Key == 0x01000000 && _kind == WindowKind.Popup && _popup?.GrabFocus == true)
                    {
                        _client.OnDismissed();
                        return 0;
                    }
                    return msg == Win32.WM_SYSKEYDOWN ? null : 0;
                }
            case Win32.WM_KEYUP or Win32.WM_SYSKEYUP:
                {
                    var e = new KeyEvent { Key = Keys.ToQt((int)wParam), Modifiers = Keys.Modifiers() };
                    scene.KeyRelease(e);
                    return msg == Win32.WM_SYSKEYUP ? null : 0;
                }
            case ImeSession.WmImeSetContext:
                return Win32.DefWindowProcW(Handle, msg, wParam, ImeSession.HideDefaultCompositionWindow(lParam));
            case ImeSession.WmImeNotify:
                if ((int)wParam is ImeSession.ImnOpenCandidate or ImeSession.ImnChangeCandidate)
                    PlaceIme(scene);
                return null;
            case ImeSession.WmImeStartComposition:
                PlaceIme(scene);
                return 0;
            case ImeSession.WmImeEndComposition:
                ApplyIme(scene, _ime.OnEnd());
                return 0;
            case ImeSession.WmImeComposition:
                {
                    var flags = (int)lParam;
                    var text = "";
                    if ((flags & (ImeSession.GcsResultStr | ImeSession.GcsCompStr)) != 0)
                    {
                        var index = (flags & ImeSession.GcsResultStr) != 0 ? ImeSession.GcsResultStr : ImeSession.GcsCompStr;
                        text = ImeNative.CompositionString(Handle, index);
                    }
                    ApplyIme(scene, _ime.OnComposition(flags, text));
                    PlaceIme(scene);
                    return 0;
                }
            case ImeSession.WmImeChar:
                {
                    var action = _ime.OnImeChar((char)wParam);
                    if (action.Kind == ImeActionKind.Insert)
                        InsertImeChar(scene, action.Text[0]);
                    return 0;
                }
            case Win32.WM_CHAR:
                {
                    var c = (char)wParam;
                    if (char.IsHighSurrogate(c))
                    {
                        _pendingHigh = c;
                        return 0;
                    }
                    var text = _pendingHigh is { } high ? new string([high, c]) : c.ToString();
                    _pendingHigh = null;
                    if (!char.IsControl(text[0]))
                        scene.TextInput(text);
                    return 0;
                }
            case Win32.WM_ACTIVATE:
                // A grabbing popup closes on a click outside it, which the mouse hook sees; losing activation
                // alone does not close it, since Windows can take activation away at any time.
                return null;
            case Win32.WM_KILLFOCUS:
                scene.SetFocusItem(scene.FocusItem is { } focused && _kind == WindowKind.Floating ? focused : null);
                return null;
            case Win32.WM_GETMINMAXINFO when _floating is { } f:
                {
                    var info = (MINMAXINFO*)lParam;
                    if (f.MinimumSize.Width > 0)
                        info->ptMinTrackSize.X = (int)(f.MinimumSize.Width * _scale);
                    if (f.MinimumSize.Height > 0)
                        info->ptMinTrackSize.Y = (int)(f.MinimumSize.Height * _scale);
                    if (f.MaximumSize.Width > 0)
                        info->ptMaxTrackSize.X = (int)(f.MaximumSize.Width * _scale);
                    if (f.MaximumSize.Height > 0)
                        info->ptMaxTrackSize.Y = (int)(f.MaximumSize.Height * _scale);
                    return 0;
                }
            case Win32.WM_CLOSE:
                _client.OnClosed();
                SetVisible(false);
                return 0;
            case Win32.WM_ERASEBKGND:
                return 1;
            case Win32.WM_PAINT:
                RenderNow();
                return null;
            case Win32.WM_DISPLAYCHANGE or Win32.WM_SETTINGCHANGE or Win32.WM_DWMCOLORIZATIONCOLORCHANGED or Win32.WM_SYSCOLORCHANGE or Win32.WM_THEMECHANGED:
                _platform.SystemChanged(msg, wParam);
                return null;
            case Win32.WM_GETOBJECT:
                return _automation.GetObject(wParam, lParam);
        }
        if (msg == WindowAutomation.RunMessage)
        {
            _automation.RunPosted(lParam);
            return 0;
        }
        if (msg == TaskbarCreated && _kind == WindowKind.Panel)
        {
            // Explorer restarted and forgot every app bar: reserve the edge again.
            RemoveAppBar();
            _platform.Loop.Post(PlacePanel);
            return 0;
        }
        if (msg == AppBarMessage)
        {
            switch ((uint)wParam)
            {
                case Win32.ABN_POSCHANGED:
                    // A burst of these, as each ABM_SETPOS sends, places the panel once. A notice that is not the
                    // one our own SETPOS just caused means some other bar moved, so a short grant is asked for again.
                    _posNotices.Delivered();
                    if (_posNotices.TakeExternal())
                        _appBar.NoteExternalChange();
                    if (!_placePosted)
                    {
                        _placePosted = true;
                        _platform.Loop.Post(() =>
                        {
                            _placePosted = false;
                            PlacePanel();
                        });
                    }
                    break;
                case Win32.ABN_FULLSCREENAPP:
                    // Step back while another app is fullscreen on this monitor, as the taskbar does.
                    _fullscreenAppActive = lParam != 0;
                    _platform.Loop.Post(PlacePanel);
                    break;
            }
            return 0;
        }
        return null;
    }

    private char? _pendingHigh;
    private readonly ImeSession _ime = new();

    private void ApplyIme(Scene scene, ImeAction action)
    {
        switch (action.Kind)
        {
            case ImeActionKind.Preedit:
                scene.SetComposition(action.Text);
                break;
            case ImeActionKind.Commit:
                scene.CommitComposition(action.Text);
                break;
            case ImeActionKind.Cancel:
                scene.CancelComposition();
                break;
            case ImeActionKind.Insert:
                if (action.Text.Length > 0)
                    InsertImeChar(scene, action.Text[0]);
                break;
        }
    }

    private void InsertImeChar(Scene scene, char c)
    {
        if (char.IsHighSurrogate(c))
        {
            _pendingHigh = c;
            return;
        }
        var text = _pendingHigh is { } high ? new string([high, c]) : c.ToString();
        _pendingHigh = null;
        if (!char.IsControl(text[0]))
            scene.TextInput(text);
    }

    private void PlaceIme(Scene scene)
    {
        var caret = scene.ImeCaret();
        ImeNative.Place(Handle, (int)Math.Round(caret.X * _scale), (int)Math.Round((caret.Y + caret.Height) * _scale));
    }

    private (double X, double Y) Point(nint lParam) => (Win32.LowWord(lParam) / _scale, Win32.HighWord(lParam) / _scale);

    private static int Button(uint msg, nint wParam) => msg switch
    {
        Win32.WM_LBUTTONDOWN or Win32.WM_LBUTTONUP or Win32.WM_LBUTTONDBLCLK => QtEnums.LeftButton,
        Win32.WM_RBUTTONDOWN or Win32.WM_RBUTTONUP or Win32.WM_RBUTTONDBLCLK => QtEnums.RightButton,
        Win32.WM_MBUTTONDOWN or Win32.WM_MBUTTONUP or Win32.WM_MBUTTONDBLCLK => QtEnums.MiddleButton,
        _ => Win32.HighWord(wParam) == 1 ? QtEnums.BackButton : QtEnums.ForwardButton,
    };

    /// <summary>Whether a point on the virtual desktop, in physical pixels, is inside this window.</summary>
    internal bool ContainsScreenPoint(POINT pt)
    {
        Win32.GetWindowRect(Handle, out var r);
        return pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
    }

    internal void Dismiss() => _client.OnDismissed();

    public void Dispose()
    {
        if (Handle == 0)
            return;
        _platform.UpdatePopupGrab(this, false);
        RemoveAppBar();
        _automation.Dispose();
        _surface?.Dispose();
        _surface = null;
        var h = Handle;
        Win32.SetWindowLongPtr(h, Win32.GWLP_USERDATA, 0);
        Handle = 0;
        Win32.DestroyWindow(h);
        if (_self.IsAllocated)
            _self.Free();
        _platform.Forget(this);
    }
}
