using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinRT;
using Winshell.Native;

namespace Winshell;

/// <summary>The native window behind a shell window, with a XAML island where its content goes.</summary>
internal sealed unsafe class HostWindow : IDisposable
{
    private const string ClassName = Diagnostics.DoctorScreens.WinshellClass;
    private static bool _registered;

    private readonly ShellPlatform _platform;
    private readonly WindowKind _kind;
    private readonly Action _dismissMenu;
    private readonly Grid _root = new() { Background = null };
    private readonly IHostClient _client;
    private readonly AppBar? _appBar;
    private readonly FloatingFrame? _frame;
    private GCHandle _self;
    private DesktopWindowXamlSource? _xaml;
    private SystemBackdropConfiguration? _backdropConfig;
    private ISystemBackdropControllerWithTargets? _backdropController;
    private bool _visible;
    private PanelPlacement? _panel;
    private PopupPlacement? _popup;
    private bool _released;
    private bool _clickThrough;
    private Point _origin;
    private string _screenId = "";
    private bool _placing;
    private bool _failed;

    public HostWindow(ShellPlatform platform, WindowKind kind, IHostClient client, Action dismissMenu)
    {
        _platform = platform;
        _kind = kind;
        _client = client;
        _dismissMenu = dismissMenu;
        Register();
        var (style, exStyle) = Styles(kind);
        Handle = Win32.CreateWindowExW(exStyle, ClassName, "", style, 0, 0, 1, 1, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (Handle == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        if (kind == WindowKind.Panel)
            _appBar = new AppBar(Handle, platform, PlacePanel);
        else if (kind == WindowKind.Floating)
            _frame = new FloatingFrame(this, platform);
        _self = GCHandle.Alloc(this);
        Win32.SetWindowLongPtr(Handle, Win32.GWLP_USERDATA, GCHandle.ToIntPtr(_self));
        // A window that could not be finished is destroyed here; its client never gets it to dispose.
        try
        {
            Scale = Win32.GetDpiForWindow(Handle) / 96.0;
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
            ApplyCaption();
            _root.KeyDown += OnRootKeyDown;
            _root.Tag = client;
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

    /// <summary>The shell window this native window shows.</summary>
    internal IHostClient Client => _client;

    /// <summary>Whether this window has an app bar with Explorer, which reserves its edge.</summary>
    internal bool HoldsEdge => _appBar?.Registered == true;

    public WindowKind Kind => _kind;

    public bool IsVisible => _visible;

    public Point Origin => _origin;

    public double Scale { get; internal set; } = 1;

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
        ApplyCaption();
        if (_backdropConfig is { } config)
        {
            config.Theme = _platform.BackdropTheme;
            config.IsHighContrast = _platform.IsHighContrast;
        }
    }

    /// <summary>The caption follows the shell's dark or light choice, including a window that has a title bar.</summary>
    private void ApplyCaption()
    {
        if (Handle == 0)
            return;
        var dark = _platform.IsDark ? 1 : 0;
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, dark, 4);
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _kind == WindowKind.Popup && _popup?.GrabFocus == true)
        {
            _client.OnDismissed();
            e.Handled = true;
        }
    }

    /// <summary>A panel or popup press ends another app's native menu before its control handles the click.</summary>
    internal void Press()
    {
        if (_kind is WindowKind.Panel or WindowKind.Popup)
            _dismissMenu();
    }

    internal static (uint Style, uint ExStyle) Styles(WindowKind kind) => kind switch
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
        SetExStyle(Win32.WS_EX_NOACTIVATE, !placement.Focusable);
        PlacePanel();
    }

    /// <summary>Puts the panel on its screen, reserving its exclusive zone through an app bar.</summary>
    internal void PlacePanel()
    {
        // Explorer's notice that another bar moved can land on the loop after this window is gone. An app bar asked
        // for with no window is one nothing can remove, and it holds the edge after the shell has exited.
        if (_released || _panel is not { } p || _appBar is not { } appBar)
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
        var topmost = p.AboveWindows && !appBar.FullscreenAppActive;
        SetExStyle(Win32.WS_EX_TOPMOST, topmost);
        // HWND_NOTOPMOST would put it above every window that is not topmost, so over the fullscreen app.
        var after = topmost ? Win32.HWND_TOPMOST : Win32.HWND_BOTTOM;
        Move(rect, after, Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    public void SetPopup(PopupPlacement placement)
    {
        _popup = placement;
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
        _platform.UpdatePopupGrab(this, _visible && placement.GrabFocus);
    }

    public void SetFloating(FloatingPlacement placement)
    {
        if (Handle != 0 && !_released)
            _frame?.Apply(placement);
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
        var pt = new POINT { X = (int)Math.Round(x * Scale), Y = (int)Math.Round(y * Scale) };
        Win32.ClientToScreen(Handle, ref pt);
        return (pt.X, pt.Y);
    }

    /// <summary>Moves the window to a rectangle in physical pixels, then tells its client the size and place it got.</summary>
    internal void Move(RECT rect, nint after, uint flags)
    {
        // Moving onto a monitor of another DPI sends WM_DPICHANGED from inside SetWindowPos; this rect is already right for it.
        _placing = true;
        try
        {
            Win32.SetWindowPos(Handle, after, rect.Left, rect.Top, rect.Width, rect.Height, flags);
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

    internal void UpdateSize()
    {
        Win32.GetClientRect(Handle, out var rc);
        var origin = new POINT();
        Win32.ClientToScreen(Handle, ref origin);
        var (device, monitor) = Screens.Of(Handle);
        _origin = DesktopSpace.ToLogical(origin, monitor, Scale);
        var width = Math.Max(1, rc.Width);
        var height = Math.Max(1, rc.Height);
        _xaml?.SiteBridge.MoveAndResize(new RectInt32(0, 0, width, height));
        _client.OnResized(width / Scale, height / Scale, Scale);
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
            // A process's first SW_SHOW takes the state its launcher asked for, so a hidden start would hide the window.
            if (!Win32.IsWindowVisible(Handle))
                Win32.ShowWindow(Handle, show);
            if (_kind == WindowKind.Floating && !Win32.IsIconic(Handle))
                OwnedWindows.Raise(Handle, _platform.Windows);
            if (_kind == WindowKind.Popup && _popup?.GrabFocus == true)
                Win32.SetForegroundWindow(Handle);
        }
        else
        {
            _appBar?.Remove();
            Win32.ShowWindow(Handle, Win32.SW_HIDE);
        }
        if (_kind == WindowKind.Popup)
            _platform.UpdatePopupGrab(this, visible && _popup?.GrabFocus == true);
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
            Win32.SetLayeredWindowAttributes(Handle, 0, 255, 2);
    }

    /// <summary>Fades the content. The material behind it does not fade, and has no opacity of its own.</summary>
    public void SetOpacity(double opacity) => _root.Opacity = opacity;

    /// <summary>Clips the content to a rectangle in logical window coordinates. It does not change <see cref="SetInputRegion"/>.</summary>
    public void SetVisibleMask(Rect? bounds) => _root.Clip = bounds is { } b
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
        if (_frame is { } frame)
            frame.SetCorners(corner);
        else
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
        if (_kind == WindowKind.Panel && _panel?.Focusable != true)
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
                if (!_placing && !_released)
                    _frame?.OnMovedOrResized();
                return null;
            case Win32.WM_DPICHANGED:
                {
                    // A size we apply from this message can send it again with the monitor's old scale. Keep the one that started it.
                    if (!_placing)
                        Scale = DesktopSpace.Scale((uint)Win32.HighWord(wParam));
                    if (_frame is { } frame)
                        frame.OnDpiChanged(*(RECT*)lParam, _placing);
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
                // A click here does not close a menu another app opened without the foreground, as tray menus
                // often are. Close it before the mouse message reaches the XAML island.
                Press();
                if (_kind == WindowKind.Panel && _panel?.Focusable != true)
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
            case Win32.WM_GETMINMAXINFO when _frame is { } frame:
                return frame.OnGetMinMaxInfo((MINMAXINFO*)lParam);
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
                if (msg == Win32.WM_DISPLAYCHANGE)
                    _frame?.OnDisplayChanged();
                _platform.SystemChanged(msg, wParam);
                return null;
        }
        // Explorer's notices to a panel's app bar.
        return _appBar?.OnMessage(msg, wParam, lParam);
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
        Cleanup(destroyWindow: false);
        _platform.Loop.Post(_client.OnBackingDestroyed);
    }

    private void Cleanup(bool destroyWindow)
    {
        if (_released)
            return;
        _released = true;
        _platform.ThemeChanged -= OnThemeChanged;
        _platform.UpdatePopupGrab(this, false);
        _appBar?.Remove();
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
