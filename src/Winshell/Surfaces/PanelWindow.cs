using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Winshell;

/// <summary>
/// The popup a bar widget opens. Arrow keys, Return, Tab, and Escape work inside it.
/// <see cref="Session"/> decides when it opens and closes; this window draws, slides, and reports what Windows tells it.
/// </summary>
internal sealed partial class PanelWindow : Window, IPanelWindow
{
    private readonly StackPanel _rows = new() { Spacing = Spacing.RowGap };
    private readonly Border _frame;
    private readonly ScrollViewer _scroll;
    private readonly ShellBackdrop _backdrop = new();
    private readonly OutsideClickWatch _outside = new();
    private nint _ownerHwnd;
    private Window? _host;
    private FrameworkElement? _anchor;
    private Windows.Foundation.Rect? _at;
    private BarEdge _edge;
    private int _fitWidth = -1;
    private int _fitHeight = -1;
    private readonly PanelSession _session;
    private bool _ready;
    private const int KeyboardTries = 40;
    private static readonly TimeSpan KeyboardWait = TimeSpan.FromMilliseconds(40);
    private bool _middle;
    private bool _above = true;
    private Func<nint, bool>? _focus;
    private Func<nint, bool>? _seek;

    /// <summary>Tab asked for the next panel. Positive walks forward. Shift+Tab is negative.</summary>
    internal event Action<int>? CyclePanel;

    public PanelWindow()
    {
        // Named, so window lists and automation tools can tell the panel from the bar.
        Title = "Winoma panel";
        _session = new PanelSession(this);
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // The click is on the bar, which does not take focus, so this window deactivates.
                // That is the toggle click, not an outside press. Closing here makes the click open it again.
                _session.WindowDeactivated(ButtonDownOnBar());
                return;
            }

            _session.WindowActivated();
        };
        _frame = new Border
        {
            Background = Paint.Background,
            CornerRadius = Corners.Overlay,
            Child = _scroll = new ScrollViewer
            {
                // The ring around the selected control lies over the rows and scrolls with them.
                Content = new Grid { Children = { _rows, _ring.Element } },
                MaxHeight = 480,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        // On the lane, which is the window's whole content. For a moment after the window opens the keyboard is on the lane
        // and not yet in the rows, and a key pressed then must still be heard.
        _lane.PreviewKeyDown += OnPreviewKey;
        _frame.KeyDown += OnKey;
        _lane.CharacterReceived += OnCharacter;

        // A press of the pointer takes over from the arrow keys. A button marks its press handled, and this must still hear it.
        _lane.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Deselect()), handledEventsToo: true);
        // The rows are measured as they are built, before their templates and text have laid out once. Fit again once they have.
        _rows.SizeChanged += (_, _) =>
        {
            if (_kitPanel is not null)
            {
                Fit(_kitPanel.Width, _kitPanel.Height, 1, hold: false);
            }
        };
        Pad(true);
        PrepareLane();
        _scroll.XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;
    }

    /// <summary>Keeps the gutter between the panel's edge and its rows on every side, or none for a panel that draws to its edge.</summary>
    private void Pad(bool padded)
    {
        var gutter = padded ? Spacing.PanelPadding : 0;
        // The sides of the inset sit inside the scroll area, so the scrollbar runs along the panel's edge.
        _rows.Margin = new Thickness(gutter, 0, gutter, 0);
        _frame.Padding = new Thickness(0, gutter, 0, gutter);
    }

    /// <summary>Opens and closes this panel.</summary>
    internal PanelSession Session => _session;

    /// <summary>
    /// Notes the bar and the item the panel opens from, and makes the window a borderless popup the first time.
    /// <paramref name="at"/> is a place on the bar, in its own device-independent pixels, that stands in for the item.
    /// </summary>
    private void Attach(Window owner, BarEdge edge, FrameworkElement anchor, Windows.Foundation.Rect? at)
    {
        _ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        _host = owner;
        _edge = edge;
        _anchor = anchor;
        _at = at;
        if (_ready)
        {
            return;
        }

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        ((FrameworkElement)Content).IsTabStop = true;
        _ready = true;
    }

    private void Scrolls(bool scrolls)
    {
        _scroll.VerticalScrollBarVisibility = scrolls ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        _scroll.VerticalScrollMode = scrolls ? ScrollMode.Enabled : ScrollMode.Disabled;
    }

    /// <summary>Sizes the flyout to <paramref name="width"/>, and to <paramref name="height"/> or else the rows it measures. A second call with the same size leaves the window where it is. <paramref name="hold"/> skips seating so the entrance can tuck the card under the bar.</summary>
    private void Fit(int width, int height, int rows, bool hold)
    {
        if (_ownerHwnd == 0 || _anchor is null || _host is null)
        {
            return;
        }

        var widthDips = width > 0 ? width : 340;
        var heightDips = height > 0
            ? height
            : MeasuredHeight(widthDips) ?? Math.Clamp(72 + (rows * 56), 120, PanelPlacement.MaximumHeight);
        // The size is the one the rows fill. Windows keeps its outline out of the client area, so the window is that much larger.
        // Without it the rows lose the outline from their right and bottom, and the padding there is not the padding at the left and top.
        var outlineWidth = PanelPlacement.Outline(AppWindow.Size.Width, AppWindow.ClientSize.Width);
        var outlineHeight = PanelPlacement.Outline(AppWindow.Size.Height, AppWindow.ClientSize.Height);
        var widthPx = ScreenMetrics.Dips(_ownerHwnd, widthDips) + outlineWidth;
        var heightPx = ScreenMetrics.Dips(_ownerHwnd, heightDips) + outlineHeight;
        if (widthPx == _specWidth && heightPx == _specHeight)
        {
            return;
        }

        _specWidth = widthPx;
        _specHeight = heightPx;
        var roomPx = _kitPanel is { Room: > 0 } grows ? ScreenMetrics.Dips(_ownerHwnd, grows.Room) + outlineHeight : 0;
        var fitted = Place(_ownerHwnd, _edge, _anchor, _host, widthPx, heightPx, roomPx);
        var scale = Math.Max(1, ScreenMetrics.Dips(_ownerHwnd, 100)) / 100.0;
        // The heading scrolls with the rows, so only the frame's padding and border sit outside the scroll area.
        var reserve = _frame.Padding.Top + _frame.Padding.Bottom + _frame.BorderThickness.Top + _frame.BorderThickness.Bottom;
        _scroll.MaxHeight = Math.Max(32, ((fitted - outlineHeight) / scale) - reserve);
        if (!hold)
        {
            Settle();
        }
    }

    /// <summary>How tall the rows are at <paramref name="widthDips"/>, with the frame's padding and border. Null before they can be measured.</summary>
    private int? MeasuredHeight(int widthDips)
    {
        var padding = _frame.Padding;
        var border = _frame.BorderThickness;
        var inner = Math.Max(0, widthDips - padding.Left - padding.Right - border.Left - border.Right);
        _rows.Measure(new Windows.Foundation.Size(inner, double.PositiveInfinity));
        return PanelPlacement.FitHeight(_rows.DesiredSize.Height, padding.Top + padding.Bottom + border.Top + border.Bottom);
    }

    /// <summary>Dresses in the palette in effect, with acrylic when transparency effects are on.</summary>
    public void ApplyChrome()
    {
        var chrome = Paint.Chrome;
        _frame.RequestedTheme = chrome.HighContrast
            ? ElementTheme.Default
            : chrome.Light ? ElementTheme.Light : ElementTheme.Dark;
        var acrylic = !chrome.HighContrast && chrome.Effects && ShellBackdrop.IsSupported;
        try
        {
            if (acrylic)
            {
                _backdrop.Configure(chrome.Light, chrome.Background, 0.85f, 0.96f);

                // A hidden panel holds no acrylic controller (FinishHide). A theme change while it is hidden leaves that so,
                // and the next open comes here again with the window shown.
                if (SystemBackdrop != _backdrop && AppWindow.IsVisible)
                {
                    SystemBackdrop = _backdrop;
                }
            }
            else if (SystemBackdrop is not null)
            {
                SystemBackdrop = null;
            }
        }
        catch (Exception ex)
        {
            acrylic = false;
            SystemBackdrop = null;
            Log.Warning("Theme", "panel acrylic failed", ex);
        }

        _frame.Background = acrylic ? Paint.Hit : Paint.Background;
        PanelBackfill.Use(WinRT.Interop.WindowNative.GetWindowHandle(this), chrome.Background.R, chrome.Background.G, chrome.Background.B);
        // Windows draws the outline on its own rounded clip, so the corners are as thick as the sides.
        BorderlessFrame.Round(WinRT.Interop.WindowNative.GetWindowHandle(this), chrome.Light, outline: true);
    }

    /// <summary>
    /// Keeps an open panel above other windows, or lets a fullscreen window cover it.
    /// A panel at the middle of the screen stays above: it was asked for by a chord, over whatever is there.
    /// </summary>
    public void KeepAbove(bool above)
    {
        _above = above;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = above || _middle;
        }
    }

    /// <summary>
    /// The overflow cell under a screen point, when this panel is showing the tray grid.
    /// </summary>
    /// <param name="screenX">The pointer, in physical pixels.</param>
    /// <param name="screenY">The pointer, in physical pixels.</param>
    /// <param name="cell">The cell a drop there lands on.</param>
    /// <returns>True when the point is inside this window and the overflow grid is up.</returns>
    internal bool TryTrayCell(int screenX, int screenY, out int cell)
    {
        cell = 0;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == 0 || Content is not UIElement root || !PointerProbe.TryWindow(hwnd, out var rect) || !rect.Contains(screenX, screenY))
        {
            return false;
        }

        var scale = Math.Max(ScreenMetrics.Scale(hwnd), 1);
        return TryCell(root, (screenX - rect.Left) / scale, (screenY - rect.Top) / scale, out cell);
    }

    /// <summary>The cell under a point in the panel, when the overflow grid is in the tree.</summary>
    private static bool TryCell(UIElement root, double x, double y, out int cell)
    {
        cell = 0;
        var grid = FindTray(root);
        if (grid is null)
        {
            return false;
        }

        var local = root.TransformToVisual(grid).TransformPoint(new Windows.Foundation.Point(x, y));
        cell = TrayCells.Cell(local.X, local.Y, TrayButtons(grid));
        return true;
    }

    private static int TrayButtons(DependencyObject grid)
    {
        var count = 0;
        var children = VisualTreeHelper.GetChildrenCount(grid);
        for (var i = 0; i < children; i++)
        {
            if (VisualTreeHelper.GetChild(grid, i) is Button)
            {
                count++;
            }
        }

        return count;
    }

    private static FrameworkElement? FindTray(DependencyObject node)
    {
        if (node is FrameworkElement element && AutomationProperties.GetAutomationId(element) == TrayCells.AutomationId)
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var found = FindTray(VisualTreeHelper.GetChild(node, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Gives a window the keyboard when the panel asks for it after a Workspace switch.</summary>
    /// <param name="focus">Gives a window the keyboard. True when it has it.</param>
    internal void UseFocus(Func<nint, bool> focus) => _focus = focus;

    /// <summary>Gives a window the keyboard when the Menu opens. It is asked every few moments, so it must keep no record of a refusal.</summary>
    /// <param name="seek">Gives a window the keyboard. True when it has it.</param>
    internal void UseSeek(Func<nint, bool> seek) => _seek = seek;

    /// <summary>
    /// Says whether a screen point is on a Bar, any monitor's. A press on a Bar is that Bar's to report, with which Bar it is:
    /// on this panel's own Bar an item closes this panel or opens its own, and on another monitor's the press only dismisses
    /// (<see cref="PanelSession.PointerDown"/>, <see cref="PanelSession.BarPressed"/>). The watch for presses anywhere on screen
    /// therefore leaves every Bar alone. Were it to dismiss for a press on another Bar itself, the session would have no panel up
    /// by the time that Bar reported the press, and the release would open a panel there.
    /// </summary>
    /// <param name="onBar">True for a point, in physical pixels, inside a Bar's window.</param>
    internal void UseBars(Func<int, int, bool> onBar) => _onBar = onBar;

    private Func<int, int, bool>? _onBar;

    private void PressedAt(int x, int y)
    {
        var panel = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _session.PressedOutside(Inside(panel, x, y), OnBar(x, y));
    }

    private bool OnBar(int x, int y) => Inside(_ownerHwnd, x, y) || (_onBar?.Invoke(x, y) ?? false);

    private static bool Inside(nint hwnd, int x, int y)
    {
        return hwnd != 0 && PointerProbe.TryWindow(hwnd, out var rect) && rect.Contains(x, y);
    }

    /// <summary>True while the left button is down on a bar.</summary>
    private bool ButtonDownOnBar()
    {
        if (!PointerProbe.IsDown(PointerProbe.LeftButton) || !PointerProbe.TryCursor(out var x, out var y))
        {
            return false;
        }

        return OnBar(x, y);
    }

    private void OnPreviewKey(object sender, KeyRoutedEventArgs args)
    {
        // A modifier on its own is no key to offer, and it would refresh the panel on every press of Shift.
        var key = new KitKey(args.Key, PointerProbe.IsDown(PointerProbe.Control), ShiftDown(), PointerProbe.IsDown(PointerProbe.Alt), args.KeyStatus.WasKeyDown);
        if (args.Key is not (VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows)
            && OfferKey(key))
        {
            args.Handled = true;
            return;
        }

        // An arrow key or Return the plugin left alone moves the Kit's selection. With a modifier it is some other key.
        if (!key.Ctrl && !key.Shift && !key.Alt && Selects(args.Key) is { } move
            && SelectKey(move, key.Repeat, args.OriginalSource as UIElement, typing: true))
        {
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.Tab)
        {
            CyclePanel?.Invoke(ShiftDown() ? -1 : 1);
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.Escape)
        {
            _session.PressEscape();
            args.Handled = true;
        }
    }

    /// <summary>
    /// A letter typed before the panel's search line had the keyboard. For a moment after the window opens the keyboard is on the lane,
    /// where a letter would be lost, and the Menu is typed into at once. The letter is handed to the line.
    /// </summary>
    private void OnCharacter(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        // The event comes up from whatever has the keyboard. A letter typed into a field, a button, or a slider of the panel is that control's.
        if (args.OriginalSource is Control control && !ReferenceEquals(control, _scroll))
        {
            return;
        }

        if (_kit?.TypeAhead(args.Character) == true)
        {
            args.Handled = true;
        }
    }

    private void OnKey(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            _session.PressEscape();
            args.Handled = true;
        }
    }

    void IPanelWindow.Post(Action work) => DispatcherQueue.TryEnqueue(() => work());

    void IPanelWindow.After(TimeSpan delay, Action work)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (tick, _) =>
        {
            tick.Stop();
            work();
        };
        timer.Start();
    }

    void IPanelWindow.Raise(bool first)
    {
        Activate();
        if (first)
        {
            ApplyChrome();
            if (_middle)
            {
                SeekKeyboard(KeyboardTries);
            }
        }

        KeepUnderBar();
    }

    /// <summary>
    /// A panel a chord opened at the middle of the screen is typed into at once, and Windows does not hand the keyboard to a window
    /// just because it asked. This asks through the gateway, which knows when it may join the window in front (ADR 0020), and asks again
    /// until the panel has the keyboard: Super is often still held on the first tries. The ask after a Workspace switch backs off for a
    /// second once Windows says no, which would make every later try here a no-op, so these tries go through an ask that does not.
    /// </summary>
    private void SeekKeyboard(int tries)
    {
        if (!_middle || !_session.Live || (_seek ?? _focus) is not { } seek)
        {
            return;
        }

        if (seek(WinRT.Interop.WindowNative.GetWindowHandle(this)))
        {
            return;
        }

        if (tries <= 1)
        {
            Log.Warning("Panel", "the Menu did not get the keyboard in " + (KeyboardTries * KeyboardWait.TotalMilliseconds) + " ms");
            return;
        }

        ((IPanelWindow)this).After(KeyboardWait, () => SeekKeyboard(tries - 1));
    }

    void IPanelWindow.KeepUnderBar() => KeepUnderBar();

    // A click anywhere else is raw input on a side thread: Windows has already moved the cursor,
    // so the bar's thread is not in the pointer path. The bar reports its own presses because it does not activate.
    void IPanelWindow.ListenOutside() => _outside.Listen((x, y) => DispatcherQueue.TryEnqueue(() => PressedAt(x, y)));

    bool IPanelWindow.Leave(Action finished)
    {
        _outside.Silence();
        ForgetKit();
        if (BeginLeave(finished))
        {
            return true;
        }

        FinishHide();
        return false;
    }

    void IPanelWindow.TakeKeyboard() => _focus?.Invoke(WinRT.Interop.WindowNative.GetWindowHandle(this));

    private static bool ShiftDown() => PointerProbe.IsDown(PointerProbe.Shift);

    /// <summary>Notes where a panel of this size rests. <paramref name="roomPx"/> is the height it grows to while open, or zero.</summary>
    private int Place(nint hwnd, BarEdge edge, FrameworkElement anchor, Window owner, int widthPx, int heightPx, int roomPx)
    {
        _ = PointerProbe.TryWindow(hwnd, out var rect);
        var screen = ScreenMetrics.Of(hwnd);
        var margin = ScreenMetrics.Dips(hwnd, 8);
        var maxWidth = Math.Max(margin, screen.Width - (margin * 2));
        var maxHeight = Math.Max(margin, screen.Height - (margin * 2));
        var area = new PixelRect(screen.X, screen.Y, screen.Width, screen.Height);
        var bar = new PixelRect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        var gap = ScreenMetrics.Dips(hwnd, PanelPlacement.Gap);
        if (!_middle)
        {
            // A panel beside the bar has the bar and its gap less room than the screen. One capped at the screen alone is slid back over the bar.
            (maxWidth, maxHeight) = PanelPlacement.Largest(edge, bar, area, gap);
        }

        widthPx = Math.Min(widthPx, maxWidth);
        heightPx = Math.Min(heightPx, maxHeight);
        _fitWidth = widthPx;
        _fitHeight = heightPx;
        if (_middle)
        {
            _rest = PanelPlacement.Middle(area, widthPx, heightPx, gap, Math.Min(roomPx, maxHeight));
            return heightPx;
        }

        // A place on the bar stands in for the item: the pointer of a right click on the bar itself, which has no size.
        var root = owner.Content as UIElement;
        var item = _at
            ?? new Windows.Foundation.Rect(
                root is null ? new Windows.Foundation.Point() : anchor.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0)),
                new Windows.Foundation.Size(Length(anchor.ActualWidth), Length(anchor.ActualHeight)));
        var widgetX = rect.Left + ScreenMetrics.Dips(hwnd, (int)item.X);
        var widgetY = rect.Top + ScreenMetrics.Dips(hwnd, (int)item.Y);
        _rest = PanelPlacement.Beside(
            edge,
            bar,
            new PixelRect(widgetX, widgetY, Span(hwnd, item.Width), Span(hwnd, item.Height)),
            widthPx,
            heightPx,
            area,
            gap);
        return heightPx;
    }

    /// <summary>A measured length, or zero before the element has been laid out.</summary>
    private static double Length(double dips) => double.IsNaN(dips) || dips < 0 ? 0 : dips;

    private static int Span(nint hwnd, double dips)
    {
        if (double.IsNaN(dips) || dips <= 0)
        {
            return 0;
        }

        return ScreenMetrics.Dips(hwnd, (int)Math.Round(dips));
    }
}

