using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT;
using Winshell.Native;
using Winshell.Windows;

namespace Winshell;

/// <summary>The native window behind a shell window, with a XAML island where its content goes.</summary>
internal sealed unsafe class HostWindow : IDisposable
{
    private const string ClassName = "WinshellHostWindow";
    private static bool _registered;
    private static readonly uint AppBarMessage = Win32.WM_APP + 1;
    private static readonly uint TaskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");

    private readonly ShellPlatform _platform;
    private readonly WindowKind _kind;
    private readonly Grid _root = new() { Background = null };
    private GCHandle _self;
    private IHostClient _client;
    private DesktopWindowXamlSource? _xaml;
    private SystemBackdropConfiguration? _backdropConfig;
    private ISystemBackdropControllerWithTargets? _backdropController;
    private double _scale = 1;
    private int _physicalWidth = 1;
    private int _physicalHeight = 1;
    private bool _visible;
    private PanelPlacement? _panel;
    private PopupPlacement? _popup;
    private FloatingPlacement? _floating;
    private bool _floatingPlaced;
    private bool _fullscreen;
    private bool _applying;
    private SavedFloatingPlace? _saved;
    private int _cornerPreference;
    private bool _released;
    private bool _toldClient;
    private bool _appBarRegistered;
    private readonly AppBarReservation _appBar = new();
    private bool _placePosted;
    private uint _appBarEdge;
    private bool _fullscreenAppActive;
    private bool _clickThrough;
    private bool _focusable;
    private QPoint _origin;
    private string _screenId = "";
    private bool _placing;
    private bool _failed;

    public HostWindow(ShellPlatform platform, WindowKind kind, IHostClient client)
    {
        _platform = platform;
        _kind = kind;
        _client = client;
        Register();
        var (style, exStyle) = Styles();
        Handle = Win32.CreateWindowExW(exStyle, ClassName, "", style, 0, 0, 1, 1, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (Handle == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        _self = GCHandle.Alloc(this);
        Win32.SetWindowLongPtr(Handle, Win32.GWLP_USERDATA, GCHandle.ToIntPtr(_self));
        // A window that could not be finished is destroyed here; its client never gets it to dispose.
        try
        {
            _scale = Win32.GetDpiForWindow(Handle) / 96.0;
            // The material shows wherever the content is transparent.
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
            _root.RequestedTheme = platform.RequestedTheme;
            _root.KeyDown += OnRootKeyDown;
            _xaml = new DesktopWindowXamlSource();
            _xaml.Initialize(Win32Interop.GetWindowIdFromWindow(Handle));
            _xaml.Content = _root;
            _xaml.SiteBridge.Show();
            platform.ThemeChanged += OnThemeChanged;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public nint Handle { get; private set; }

    public WindowKind Kind => _kind;

    public bool IsVisible => _visible;

    public QPoint Origin => _origin;

    public double Scale => _scale;

    /// <summary>What the window shows. Its background stays transparent, so the material shows through.</summary>
    public UIElement? Content
    {
        get => _root.Children.Count > 0 ? _root.Children[0] : null;
        set
        {
            _root.Children.Clear();
            if (value is not null)
                _root.Children.Add(value);
        }
    }

    public XamlRoot? XamlRoot => _root.XamlRoot;

    private void OnThemeChanged()
    {
        _root.RequestedTheme = _platform.RequestedTheme;
        if (_backdropConfig is { } config)
        {
            config.Theme = _platform.BackdropTheme;
            config.IsHighContrast = _platform.IsHighContrast;
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _kind == WindowKind.Popup && _popup?.GrabFocus == true)
        {
            _client.OnDismissed();
            e.Handled = true;
        }
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
                hIcon = AppIcon(Win32.GetSystemMetrics(Win32.SM_CXICON), Win32.GetSystemMetrics(Win32.SM_CYICON)),
                hIconSm = AppIcon(Win32.GetSystemMetrics(Win32.SM_CXSMICON), Win32.GetSystemMetrics(Win32.SM_CYSMICON)),
                hCursor = Win32.LoadCursorW(0, Win32.IDC_ARROW),
                lpszClassName = name,
            };
            Win32.RegisterClassExW(wc);
        }
        _registered = true;
    }

    /// <summary>
    /// The logo embedded in winshell.exe. The C# compiler stores <c>ApplicationIcon</c> as icon group 32512.
    /// Panels and popups share this class and are tool windows, so the icon shows on a floating window.
    /// </summary>
    private static nint AppIcon(int width, int height) =>
        Win32.LoadImageW(Win32.GetModuleHandleW(null), Win32.IDI_APPLICATION, Win32.IMAGE_ICON, width, height, 0);

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var data = Win32.GetWindowLongPtr(hwnd, Win32.GWLP_USERDATA);
        if (data != 0 && GCHandle.FromIntPtr(data).Target is HostWindow window)
        {
            // A window that threw handles nothing more; its client is making another.
            if (!window._failed)
            {
                try
                {
                    if (window.OnMessage(msg, wParam, lParam) is { } result)
                        return result;
                }
                catch (Exception e)
                {
                    window.Fail(e);
                }
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
        using var explorerFirst = ShellPlatform.ExplorerFirst();
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
        var moved = _appBar.Reserve(abd.rc, desired =>
        {
            abd.rc = desired;
            Win32.SHAppBarMessage(Win32.ABM_SETPOS, ref abd);
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
        using (ShellPlatform.ExplorerFirst())
            Win32.SHAppBarMessage(Win32.ABM_REMOVE, ref abd);
        _appBarRegistered = false;
        _appBar.Clear();
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
        _floating = placement;
        if (Handle == 0 || _released)
            return;
        _applying = true;
        try
        {
            Win32.SetWindowTextW(Handle, placement.Title);
            ApplyOwner(placement.Owner);
            if (_fullscreen && !placement.Fullscreen)
                ExitFullscreen(placement);
            else if (!_fullscreen && placement.Fullscreen)
            {
                ApplyFrame(placement);
                EnterFullscreen();
            }
            else if (_fullscreen)
            {
                // A size change while fullscreen is part of the saved place, not a new one.
                _lastFloatingWidth = placement.Width;
                _lastFloatingHeight = placement.Height;
                if (placement.Minimized && !Win32.IsIconic(Handle))
                    Win32.ShowWindow(Handle, Win32.SW_MINIMIZE);
                else if (!placement.Minimized && Win32.IsIconic(Handle))
                {
                    Win32.ShowWindow(Handle, Win32.SW_RESTORE);
                    FillMonitor();
                }
                else if (!Win32.IsIconic(Handle))
                    FillMonitor();
            }
            else
            {
                ApplyFrame(placement);
            }
            // Showing or restoring the owner can put it above the windows it owns. Put those back above it.
            if (!Win32.IsIconic(Handle))
                RaiseOwnedWindows();
            ReportFloatingState();
        }
        finally
        {
            _applying = false;
        }
    }

    private void ApplyFrame(FloatingPlacement placement)
    {
        var first = !_floatingPlaced;
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

    private void EnterFullscreen()
    {
        _saved = CapturePlace();
        _fullscreen = true;
        SetStyle(BorderlessStyle());
        // Square corners, so the window meets the monitor's corners instead of rounding inside them.
        SetCorners(1);
        if (Win32.IsIconic(Handle) || Win32.IsZoomed(Handle))
            Win32.ShowWindow(Handle, Win32.SW_RESTORE);
        FillMonitor();
    }

    private void ExitFullscreen(FloatingPlacement placement)
    {
        var saved = _saved;
        _fullscreen = false;
        _saved = null;
        _lastFloatingWidth = placement.Width;
        _lastFloatingHeight = placement.Height;
        SetCorners(_cornerPreference);
        if (saved is null)
        {
            ApplyFrame(placement);
            return;
        }
        SetStyle(saved.Style);
        Reframe();
        var monitor = Screens.Find(saved.MonitorId) ?? CurrentMonitor();
        var scale = _scale > 0 ? _scale : saved.Scale;
        if (monitor is not null && saved.Matches(monitor.Info.Id, monitor.Bounds, scale) && saved.HasPlacement)
        {
            var next = saved.Placement;
            next.ShowCmd = placement.StateRequested
                ? RequestedShow(placement)
                : Shown(saved);
            if (placement.StateRequested && !placement.Maximized)
                next.Flags &= ~Win32.WPF_RESTORETOMAXIMIZED;
            if (Win32.SetWindowPlacement(Handle, next))
            {
                UpdateSize();
                return;
            }
        }
        if (monitor is null)
        {
            ApplyFrame(placement);
            return;
        }
        var rect = saved.RectOn(monitor.Bounds, scale);
        PlaceOuter(rect);
        if (!_visible)
            return;
        var maximized = placement.StateRequested ? placement.Maximized : saved.Maximized;
        var minimized = placement.StateRequested ? placement.Minimized : saved.Minimized;
        if (minimized)
            Win32.ShowWindow(Handle, Win32.SW_MINIMIZE);
        else if (maximized)
            Win32.ShowWindow(Handle, Win32.SW_SHOWMAXIMIZED);
        else if (Win32.IsZoomed(Handle) || Win32.IsIconic(Handle))
            Win32.ShowWindow(Handle, Win32.SW_RESTORE);
    }

    private uint Shown(SavedFloatingPlace saved)
    {
        if (!_visible)
            return (uint)Win32.SW_HIDE;
        if (saved.Minimized)
            return (uint)Win32.SW_SHOWMINIMIZED;
        if (saved.Maximized)
            return (uint)Win32.SW_SHOWMAXIMIZED;
        var show = saved.HasPlacement ? saved.Placement.ShowCmd : 0;
        // A window that was never shown has no show command. Leaving fullscreen while it is visible shows it.
        return show == 0 ? (uint)Win32.SW_SHOWNORMAL : show;
    }

    private static uint RequestedShow(FloatingPlacement placement) =>
        (uint)(placement.Minimized ? Win32.SW_SHOWMINIMIZED : placement.Maximized ? Win32.SW_SHOWMAXIMIZED : Win32.SW_SHOWNORMAL);

    private SavedFloatingPlace CapturePlace()
    {
        var monitor = CurrentMonitor();
        var dpi = Win32.GetDpiForWindow(Handle);
        var scale = dpi == 0 ? monitor?.Scale ?? _scale : dpi / 96.0;
        if (scale <= 0)
            scale = 1;
        var bounds = monitor?.Bounds ?? default;
        Win32.GetWindowRect(Handle, out var shown);
        var maximized = Win32.IsZoomed(Handle);
        var minimized = Win32.IsIconic(Handle);
        var placement = new Winshell.Native.WINDOWPLACEMENT { Length = (uint)sizeof(Winshell.Native.WINDOWPLACEMENT) };
        var hasPlacement = Win32.GetWindowPlacement(Handle, ref placement);
        var normal = shown;
        if ((maximized || minimized) && hasPlacement)
            normal = RestoredScreenRect(placement, shown);
        var logical = bounds.Width > 0 ? DesktopSpace.ToLogical(normal, bounds, scale) : new QRect(0, 0, normal.Width, normal.Height);
        return new SavedFloatingPlace(
            (uint)Win32.GetWindowLongPtr(Handle, Win32.GWL_STYLE),
            scale,
            bounds,
            monitor?.Info.Id ?? "",
            logical.X - bounds.Left,
            logical.Y - bounds.Top,
            logical.Width,
            logical.Height,
            maximized,
            minimized,
            placement,
            hasPlacement);
    }

    /// <summary>
    /// The restored rectangle in screen pixels. A normal window is already there. A minimized or maximized one
    /// reports it in workspace coordinates, which start at the primary monitor's work area.
    /// </summary>
    private static RECT RestoredScreenRect(Winshell.Native.WINDOWPLACEMENT placement, RECT shown)
    {
        var primary = Screens.Enumerate().FirstOrDefault(m => m.Info.IsPrimary);
        var bias = primary is null
            ? (X: 0, Y: 0)
            : WindowRules.WorkspaceOffset(primary.Bounds, primary.Work, false);
        var shifted = WindowRules.Shift(placement.Normal, bias.X, bias.Y);
        return shifted.Width > 0 && shifted.Height > 0 ? shifted : shown;
    }

    private uint BorderlessStyle()
    {
        var style = (uint)Win32.GetWindowLongPtr(Handle, Win32.GWL_STYLE);
        var next = (style & ~Win32.WS_OVERLAPPEDWINDOW) | Win32.WS_POPUP | Win32.WS_CLIPCHILDREN;
        if ((style & Win32.WS_VISIBLE) != 0)
            next |= Win32.WS_VISIBLE;
        return next;
    }

    private void SetStyle(uint style)
    {
        if (Handle == 0)
            return;
        var current = (uint)Win32.GetWindowLongPtr(Handle, Win32.GWL_STYLE);
        // Showing and hiding go through ShowWindow. A frame change keeps whichever of those the window is in.
        if ((current & Win32.WS_VISIBLE) != 0)
            style |= Win32.WS_VISIBLE;
        else
            style &= ~Win32.WS_VISIBLE;
        if (current == style)
            return;
        Win32.SetWindowLongPtr(Handle, Win32.GWL_STYLE, (nint)style);
    }

    private void Reframe() =>
        Win32.SetWindowPos(Handle, 0, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);

    private void FillMonitor()
    {
        if (Handle == 0)
            return;
        var (_, bounds) = Screens.Of(Handle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        PlaceOuter(bounds);
    }

    private void PlaceOuter(RECT rect)
    {
        _placing = true;
        try
        {
            Win32.SetWindowPos(Handle, 0, rect.Left, rect.Top, rect.Width, rect.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
        }
        finally
        {
            _placing = false;
        }
        UpdateSize();
    }

    private void SetCorners(int preference) =>
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, preference, 4);

    private ConnectedMonitor? CurrentMonitor()
    {
        if (Handle == 0)
            return null;
        var handle = Win32.MonitorFromWindow(Handle, Win32.MONITOR_DEFAULTTONEAREST);
        return handle == 0 ? null : Screens.Describe(handle);
    }

    private void ApplyOwner(HostWindow? owner)
    {
        if (Handle == 0 || _released)
            return;
        var hwnd = owner is { Handle: not 0 } && Win32.IsWindow(owner.Handle) ? owner.Handle : 0;
        if (hwnd == Handle || (hwnd != 0 && OwnerChainContains(hwnd, Handle)))
            hwnd = 0;
        LinkOwner(hwnd);
    }


    private void LinkOwner(nint hwnd)
    {
        if (Handle == 0)
            return;
        if (hwnd != 0 && !Win32.IsWindow(hwnd))
            hwnd = 0;
        var current = Win32.GetWindowLongPtr(Handle, Win32.GWLP_HWNDPARENT);
        if (current == hwnd)
            return;
        Win32.SetWindowLongPtr(Handle, Win32.GWLP_HWNDPARENT, hwnd);
        if (hwnd != 0)
            PlaceAboveOwner();
    }

    /// <summary>Puts this window just above its owner, and leaves every other window where it is.</summary>
    private void PlaceAboveOwner()
    {
        var owner = Win32.GetWindowLongPtr(Handle, Win32.GWLP_HWNDPARENT);
        if (owner == 0 || owner == Handle || !Win32.IsWindow(owner))
            return;
        // Already directly above the owner.
        var above = Win32.GetWindow(owner, Win32.GW_HWNDPREV);
        if (above == Handle)
            return;
        // InsertAfter is the window above this one. Nothing above the owner means the top of the z-order.
        Win32.SetWindowPos(Handle, above == 0 ? Win32.HWND_TOP : above, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    private void RaiseOwnedWindows()
    {
        if (Handle == 0)
            return;
        foreach (var window in _platform.Windows)
        {
            if (window.Handle != 0 && Win32.GetWindowLongPtr(window.Handle, Win32.GWLP_HWNDPARENT) == Handle)
                window.PlaceAboveOwner();
        }
    }

    private static bool OwnerChainContains(nint owner, nint window)
    {
        for (var hop = 0; owner != 0 && hop < 16; hop++)
        {
            if (owner == window)
                return true;
            var next = Win32.GetWindowLongPtr(owner, Win32.GWLP_HWNDPARENT);
            if (next == owner)
                return false;
            owner = next;
        }
        return false;
    }

    private void ReportFloatingState()
    {
        if (_kind != WindowKind.Floating || Handle == 0 || _released)
            return;
        var fullscreen = ReadsFullscreen();
        _fullscreen = fullscreen;
        _client.OnFullscreenChanged(fullscreen);
        // A fullscreen window covers the monitor, not the work area, so it is not the framed maximized state.
        _client.OnStateChanged(Win32.IsZoomed(Handle) && !fullscreen, Win32.IsIconic(Handle));
    }

    private bool ReadsFullscreen()
    {
        if (Win32.IsIconic(Handle))
            return _fullscreen;
        var style = (uint)Win32.GetWindowLongPtr(Handle, Win32.GWL_STYLE);
        if ((style & (Win32.WS_CAPTION | Win32.WS_THICKFRAME)) != 0)
            return false;
        if (!Win32.GetWindowRect(Handle, out var rect))
            return _fullscreen;
        var (_, bounds) = Screens.Of(Handle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return _fullscreen;
        return rect.Left <= bounds.Left + 1 && rect.Top <= bounds.Top + 1 && rect.Right >= bounds.Right - 1 && rect.Bottom >= bounds.Bottom - 1;
    }

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
        _physicalWidth = Math.Max(1, rc.Width);
        _physicalHeight = Math.Max(1, rc.Height);
        _xaml?.SiteBridge.MoveAndResize(new RectInt32(0, 0, _physicalWidth, _physicalHeight));
        _client.OnResized(_physicalWidth / _scale, _physicalHeight / _scale, _scale);
        _client.OnMoved(_origin.X, _origin.Y);
        // Last, since a window that follows its screen can place itself again.
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
            var show = _kind == WindowKind.Floating ? Win32.SW_SHOW : (_kind == WindowKind.Popup && _popup?.GrabFocus == true ? Win32.SW_SHOW : Win32.SW_SHOWNOACTIVATE);
            Win32.ShowWindow(Handle, show);
            if (_kind == WindowKind.Floating && !Win32.IsIconic(Handle))
                RaiseOwnedWindows();
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
            Win32.SetLayeredWindowAttributes(Handle, 0, 255, 2);
    }

    /// <summary>Fades the content. The material behind it does not fade, and has no opacity of its own.</summary>
    public void SetOpacity(double opacity) => _root.Opacity = opacity;

    /// <summary>Clips the content to a rectangle in logical window coordinates. It does not change <see cref="SetInputRegion"/>.</summary>
    public void SetVisibleMask(QRect? bounds) => _root.Clip = bounds is { } b
        ? new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(b.X, b.Y, b.Width, b.Height) }
        : null;

    /// <summary>
    /// Puts a material behind the content. A controller told the window is active keeps a live material on a window
    /// that never activates, which the DWM backdrop attribute does not.
    /// </summary>
    public void SetBackdrop(WindowBackdrop backdrop, WindowCorners corners)
    {
        _backdropController?.Dispose();
        _backdropController = null;
        _backdropConfig = null;
        if (backdrop != WindowBackdrop.None && _xaml is not null)
        {
            _backdropConfig = new SystemBackdropConfiguration { IsInputActive = true, Theme = _platform.BackdropTheme, IsHighContrast = _platform.IsHighContrast };
            ISystemBackdropControllerWithTargets controller = backdrop switch
            {
                WindowBackdrop.Acrylic => new DesktopAcrylicController(),
                WindowBackdrop.MicaAlt => new MicaController { Kind = MicaKind.BaseAlt },
                _ => new MicaController(),
            };
            controller.AddSystemBackdropTarget(_xaml.As<ICompositionSupportsSystemBackdrop>());
            controller.SetSystemBackdropConfiguration(_backdropConfig);
            _backdropController = controller;
        }
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
        _cornerPreference = corner;
        // Fullscreen meets the monitor corners. The chosen corners come back when it leaves fullscreen.
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, _fullscreen ? 1 : corner, 4);
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
        // A Windows-key chord arrives through a low-level hook, which does not grant foreground rights, so
        // SetForegroundWindow can fail. Join the foreground thread long enough to take the keyboard, then leave it.
        if (Win32.GetForegroundWindow() != Handle && !Win32.SetForegroundWindow(Handle))
            JoinForegroundAndTakeIt();
        FocusContent();
    }

    /// <summary>Hands the keyboard to the island, unless it already has it.</summary>
    private void FocusContent()
    {
        if (_xaml is { HasFocus: false } xaml)
            xaml.NavigateFocus(new XamlSourceFocusNavigationRequest(XamlSourceFocusNavigationReason.Programmatic));
    }

    private void JoinForegroundAndTakeIt()
    {
        var foreground = Win32.GetForegroundWindow();
        var other = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
        var current = Win32.GetCurrentThreadId();
        if (other == 0 || other == current || !Win32.AttachThreadInput(current, other, true))
            return;
        try
        {
            Win32.SetForegroundWindow(Handle);
        }
        finally
        {
            Win32.AttachThreadInput(current, other, false);
        }
    }


    /// <summary>
    /// The window threw while handling a message. It stops handling them, and its client is told on the loop, outside
    /// this message, to dispose it and make another.
    /// </summary>
    private void Fail(Exception error)
    {
        if (_failed)
            return;
        _failed = true;
        _platform.Loop.Post(() =>
        {
            if (Handle != 0)
                _client.OnFailed(error);
        });
    }

    // Messages

    private nint? OnMessage(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {

            case Win32.WM_SIZE:
            case Win32.WM_MOVE:
                if (Handle != 0)
                    UpdateSize();
                if (_kind == WindowKind.Floating && !_applying && !_placing)
                    ReportFloatingState();
                return null;
            case Win32.WM_DPICHANGED:
                {
                    // A size we apply from this message can send it again with the monitor's old scale. Keep the one that started it.
                    if (!_placing)
                        _scale = DesktopSpace.Scale((uint)Win32.HighWord(wParam));
                    var suggested = *(RECT*)lParam;
                    if (_kind == WindowKind.Floating && _fullscreen)
                    {
                        // The suggested rect is the old logical size. A fullscreen window keeps covering the monitor.
                        if (!_placing)
                            FillMonitor();
                    }
                    else if (_kind == WindowKind.Floating)
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
            case Win32.WM_ACTIVATE:
                // A grabbing popup closes on a click outside it, which the mouse hook sees; losing activation
                // alone does not close it, since Windows can take activation away at any time.
                _client.OnActiveChanged(Win32.LowWord(wParam) != 0);
                return null;
            case Win32.WM_SETFOCUS:
                FocusContent();
                return null;
            case Win32.WM_GETMINMAXINFO when _fullscreen && Handle != 0:
                {
                    // The default maximum is the work area, which would stop the window short of the taskbar.
                    var (_, bounds) = Screens.Of(Handle);
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                        return null;
                    var info = (MINMAXINFO*)lParam;
                    info->ptMaxPosition.X = bounds.Left;
                    info->ptMaxPosition.Y = bounds.Top;
                    info->ptMaxSize.X = bounds.Width;
                    info->ptMaxSize.Y = bounds.Height;
                    info->ptMaxTrackSize.X = bounds.Width;
                    info->ptMaxTrackSize.Y = bounds.Height;
                    return 0;
                }
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
            case Win32.WM_DESTROY:
                // The owner is closing, so Windows destroys this window. Drop the handle before it dangles.
                NoteExternalDestroy();
                return null;
            case Win32.WM_ERASEBKGND:
                return 1;
            case Win32.WM_DISPLAYCHANGE or Win32.WM_SETTINGCHANGE or Win32.WM_DWMCOLORIZATIONCOLORCHANGED or Win32.WM_SYSCOLORCHANGE or Win32.WM_THEMECHANGED:
                if (msg == Win32.WM_DISPLAYCHANGE && _fullscreen && _kind == WindowKind.Floating)
                    FillMonitor();
                _platform.SystemChanged(msg, wParam);
                return null;
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
                    // A burst of these places the panel once. Explorer does not send one notice per own request,
                    // so each may be another bar. A short grant is asked for once more, and that same rectangle
                    // sends no notice.
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

    /// <summary>Whether a point on the virtual desktop, in physical pixels, is inside this window.</summary>
    internal bool ContainsScreenPoint(POINT pt)
    {
        Win32.GetWindowRect(Handle, out var r);
        return pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
    }

    internal void Dismiss() => _client.OnDismissed();

    public void Dispose() => Cleanup(destroyWindow: true);

    private void NoteExternalDestroy()
    {
        if (_released)
            return;
        var client = _client;
        Cleanup(destroyWindow: false);
        if (_toldClient)
            return;
        _toldClient = true;
        _platform.Loop.Post(client.OnBackingDestroyed);
    }

    private void Cleanup(bool destroyWindow)
    {
        if (_released)
            return;
        _released = true;
        _platform.ThemeChanged -= OnThemeChanged;
        _platform.UpdatePopupGrab(this, false);
        RemoveAppBar();
        _backdropController?.Dispose();
        _backdropController = null;
        _root.KeyDown -= OnRootKeyDown;
        _root.Children.Clear();
        _xaml?.Dispose();
        _xaml = null;
        var h = Handle;
        Handle = 0;
        if (h != 0)
        {
            Win32.SetWindowLongPtr(h, Win32.GWLP_USERDATA, 0);
            if (destroyWindow)
                Win32.DestroyWindow(h);
        }
        if (_self.IsAllocated)
            _self.Free();
        _platform.Forget(this);
    }
}
