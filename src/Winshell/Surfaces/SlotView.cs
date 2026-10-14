using ShellKit = Winshell.Kit.Kit;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Winshell;

/// <summary>
/// One bar slot: the pointer, the panel it opens, and hover. What it draws is a Kit <see cref="BarButton"/>,
/// or the plugin's own drawing when it implements <see cref="IKitBar"/>.
/// </summary>
internal sealed class SlotView
{
    private readonly string _id;
    private readonly PanelSession _session;
    private readonly Func<int> _bar;
    private readonly bool _verticalBar;
    private readonly BarButton _button;
    private ShellKit? _kit;
    private int _hit;
    private bool _over;
    private bool _active;
    private Action<WidgetGesture, int>? _gesture;
    private Func<bool>? _insideBar;
    private Action? _entered;
    private Action? _left;
    private BarReorder? _reorder;
    private Action<string, PointerRoutedEventArgs>? _trayDrop;
    private string _trayKey = "";
    private Point _pressedAt;
    private bool _armed;
    private bool _armedTray;
    private readonly WheelNotches _wheel = new();
    private bool _dragging;
    private bool _draggingTray;
    private bool _lifted;

    public SlotView(string id, string label, bool vertical, BarEdge edge, PanelSession session, Func<int> bar)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bar);
        _id = id;
        _session = session;
        _bar = bar;
        _verticalBar = vertical;
        Host = new BarHit
        {
            MinWidth = Spacing.BarItem,
            MinHeight = vertical ? Spacing.BarItemThin : Spacing.BarItem,
            CornerRadius = Corners.Control,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = Paint.Hit,
        };
        _button = new BarButton(Host, vertical, edge, (gesture, tag) => _gesture?.Invoke(gesture, tag));
        Host.Invoked += (_, _) =>
        {
            _hit = 0;
            if (_session.Invoke(_id))
            {
                LeftClick();
            }
        };
        Host.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            _hit = _button.MeterAt(args.GetCurrentPoint(_button.Meters).Position);
            var left = args.GetCurrentPoint(Host).Properties.IsLeftButtonPressed;
            var pinned = _button.PinnedKey(args.OriginalSource);
            EndDrag();
            _lifted = false;
            // A pinned tray icon drags into the overflow. The rest of the item still moves along the bar.
            _trayKey = pinned;
            _armedTray = left && pinned.Length > 0 && _trayDrop is not null;
            _armed = left && _reorder is not null && pinned.Length == 0;
            _pressedAt = args.GetCurrentPoint(null).Position;
            var opener =BlockPress(() => args.GetCurrentPoint(_button.Chevron).Position);
            var ownPanel = string.Equals(_session.OpenId, _id, StringComparison.Ordinal);
            if (_session.PointerDown(_id, left, PressedOnChildButton(args.OriginalSource), opener, passThrough: _button.Tray && (!left || (pinned.Length > 0 && ownPanel)), bar: _bar()))
            {
                Host.CapturePointer(args.Pointer);
            }

            // Only the left button toggles the panel. A right or middle press still reaches the plugin, which decides what it does.
            if (opener && left)
            {
                args.Handled = true;
            }
        }), true);
        Host.PointerEntered += (_, _) =>
        {
            _over = true;
            PaintHost(over: true, pressed: false);
            _gesture?.Invoke(WidgetGesture.Enter, 0);
            _entered?.Invoke();
        };
        Host.PointerExited += (_, args) =>
        {
            var position = args.GetCurrentPoint(Host).Position;
            var overHost = position.X >= 0 && position.Y >= 0 && position.X < Host.ActualWidth && position.Y < Host.ActualHeight;
            // Leaving the bar through its outer edge reports a point still inside the slot.
            // A sideways move stays in the bar and really leaves the slot, so that point is outside.
            if (overHost && (_insideBar?.Invoke() ?? true))
            {
                return;
            }

            ReleaseHover();
        };
        Host.Tapped += (_, args) =>
        {
            args.Handled = true;
            // A drag is not a click, wherever it ended.
            if (!_lifted && _session.Tap(_id))
            {
                LeftClick();
            }
        };
        Host.RightTapped += (_, args) =>
        {
            if (_button.Tray)
            {
                args.Handled = true;
                if (!_button.TrayBare && !_button.InsideChevron(args.GetPosition(_button.Chevron)))
                {
                    return;
                }

                _gesture?.Invoke(WidgetGesture.Right, 0);
                return;
            }

            if (args.Handled)
            {
                return;
            }

            args.Handled = true;
            _gesture?.Invoke(WidgetGesture.Right, _button.MeterAt(args.GetPosition(_button.Meters)));
        };
        Host.PointerWheelChanged += (_, args) =>
        {
            // A touchpad sends parts of a notch, and a sideways swipe is no scroll over an item. One gesture goes out per whole notch.
            var wheel = args.GetCurrentPoint(Host).Properties;
            args.Handled = true;
            if (wheel.IsHorizontalMouseWheel)
            {
                return;
            }

            var notches = _wheel.Turn(wheel.MouseWheelDelta, Environment.TickCount64);
            for (var i = Math.Abs(notches); i > 0; i--)
            {
                _gesture?.Invoke(notches > 0 ? WidgetGesture.ScrollUp : WidgetGesture.ScrollDown, 0);
            }
        };
        Host.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(Host).Properties.IsLeftButtonPressed && BlockPress(() => args.GetCurrentPoint(_button.Chevron).Position))
            {
                args.Handled = true;
                return;
            }

            PaintHost(over: true, pressed: true);
            if (args.GetCurrentPoint(Host).Properties.IsMiddleButtonPressed)
            {
                _gesture?.Invoke(WidgetGesture.Middle, 0);
                args.Handled = true;
            }
        };
        Host.PointerReleased += (_, args) => OnPointerReleased(args);
        // A child button handles the release, so this slot would never see it.
        // The click would stay down, and the next press would not toggle.
        Host.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) => OnPointerReleased(args)), true);
        Host.PointerCanceled += (_, args) =>
        {
            EndDrag();
            if (_session.PointerCancel(_id))
            {
                Host.ReleasePointerCapture(args.Pointer);
            }
        };
        // A press on a chip or a pinned icon is captured by that button, so its moves arrive here already handled.
        Host.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, args) => OnPointerMoved(args)), true);
        Host.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, args) =>
        {
            // Taking the pointer from a child button reports that button's loss here too. Only the slot's own loss ends the drag.
            if (ReferenceEquals(args.OriginalSource, Host))
            {
                EndDrag();
            }
        }), true);
        AutomationProperties.SetName(Host, label);
    }

    public BarHit Host { get; }

    /// <summary>The control a panel sits against. The tray uses the chevron, so the overflow opens under it.</summary>
    public FrameworkElement PanelAnchor => _button.Tray && !_button.TrayBare ? _button.Chevron : Host;

    /// <summary>True while the pointer is over this slot.</summary>
    public bool Hovered => _over;

    /// <summary>False after the plugin publishes a hidden face.</summary>
    public bool Shown => Host.Visibility == Visibility.Visible;

    /// <summary>Paints the open-panel state. A press on that control closes the panel.</summary>
    public void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;
        if (_button.SetActive(active))
        {
            return;
        }

        PaintHost(_over, pressed: false);
    }

    public void UseBar(Func<bool> insideBar) => _insideBar = insideBar;

    /// <summary>Runs when this slot's hover starts and when it ends.</summary>
    public void WatchHover(Action entered, Action left)
    {
        _entered = entered;
        _left = left;
    }

    public void ReleaseHover()
    {
        if (!_over)
        {
            return;
        }

        _over = false;
        PaintHost(over: false, pressed: false);
        _gesture?.Invoke(WidgetGesture.Leave, 0);
        _left?.Invoke();
    }

    public void Bind(Action<WidgetGesture, int> gesture) => _gesture = gesture;

    /// <summary>Lets a left press that travels pick this item up and carry it along the bar.</summary>
    public void WatchDrag(BarReorder reorder) => _reorder = reorder;

    /// <summary>Hears a pinned tray icon dropped at the pointer. The key is the icon. The arguments carry the release.</summary>
    /// <param name="drop">Called on the release, after the icon has travelled far enough to be a drag.</param>
    public void WatchTray(Action<string, PointerRoutedEventArgs> drop) => _trayDrop = drop;

    /// <summary>
    /// Draws <paramref name="view"/>'s own item in place of its face, built here for this Bar with a Kit of its own.
    /// A press on one of its controls runs through <paramref name="press"/>, which guards it and refreshes the item after it.
    /// </summary>
    /// <param name="monitor">The monitor this Bar is on, as <c>winoma desktop</c> numbers the monitors.</param>
    public void UseKit(IKitBar view, Action<string, Action> press, Func<int> monitor)
    {
        ShellKit? kit = null;
        kit = new ShellKit(
            press,
            () =>
            {
                // The drawing is built with the same Kit, so the getters of the one it replaces are dropped first.
                // They would be read on every tick for an item that is no longer on the bar.
                kit!.Release();
                _button.Hold(view.BuildBar(kit));
            },
            _verticalBar,
            monitor);
        _kit = kit;
        _button.Hold(view.BuildBar(kit));
        PaintHost(_over, pressed: false);
    }

    /// <summary>Reads the getters of a plugin's own item and writes what moved. Call on each tick.</summary>
    public void RefreshKit() => _kit?.Bindings.Refresh();

    public void Apply(BarFace face)
    {
        if (_button.Show(face))
        {
            PaintHost(_over, pressed: false);
        }
    }

    /// <summary>
    /// A few pixels of movement cancel the tap, even when the release is still on this slot.
    /// Opening from the release keeps that click. A child button has its own click.
    /// </summary>
    private void OnPointerReleased(PointerRoutedEventArgs args)
    {
        _armed = false;
        _armedTray = false;
        if (_draggingTray)
        {
            var key = _trayKey;
            _draggingTray = false;
            _trayKey = "";
            Host.ReleasePointerCapture(args.Pointer);
            _trayDrop?.Invoke(key, args);
        }

        if (_dragging)
        {
            _dragging = false;
            _reorder?.Drop(args);
            Host.ReleasePointerCapture(args.Pointer);
        }

        PaintHost(_over, pressed: false);
        var up = _session.PointerUp(_id, InsideHost(args.GetCurrentPoint(Host).Position));
        if (up.ReleaseCapture)
        {
            Host.ReleasePointerCapture(args.Pointer);
        }

        if (up.Click)
        {
            LeftClick();
        }
    }

    /// <summary>
    /// A left press that has travelled <see cref="BarDrop.Threshold"/> picks the item up. The click ends there,
    /// so the release drops the item and toggles nothing, and a child button that held the pointer does not click.
    /// </summary>
    private void OnPointerMoved(PointerRoutedEventArgs args)
    {
        if (_dragging)
        {
            _reorder?.Move(args);
            return;
        }

        if (_draggingTray)
        {
            return;
        }

        if (_armedTray && args.GetCurrentPoint(Host).Properties.IsLeftButtonPressed)
        {
            var travel = args.GetCurrentPoint(null).Position;
            if (BarDrop.Lifts(travel.X - _pressedAt.X, travel.Y - _pressedAt.Y))
            {
                _armedTray = false;
                _draggingTray = true;
                _lifted = true;
                _session.PointerCancel(_id);
                for (var node = args.OriginalSource as DependencyObject; node is not null && !ReferenceEquals(node, Host); node = VisualTreeHelper.GetParent(node))
                {
                    (node as UIElement)?.ReleasePointerCapture(args.Pointer);
                }

                Host.CapturePointer(args.Pointer);
                _gesture?.Invoke(WidgetGesture.Drag, 1);
            }

            return;
        }

        if (!_armed || _reorder is null)
        {
            return;
        }

        if (!args.GetCurrentPoint(Host).Properties.IsLeftButtonPressed)
        {
            _armed = false;
            return;
        }

        var at = args.GetCurrentPoint(null).Position;
        if (!BarDrop.Lifts(at.X - _pressedAt.X, at.Y - _pressedAt.Y))
        {
            return;
        }

        _armed = false;
        _dragging = true;
        _lifted = true;
        _session.PointerCancel(_id);
        // A chip or a pinned icon that holds the pointer would still click on the release, since the item stays under it.
        for (var node = args.OriginalSource as DependencyObject; node is not null && !ReferenceEquals(node, Host); node = VisualTreeHelper.GetParent(node))
        {
            (node as UIElement)?.ReleasePointerCapture(args.Pointer);
        }

        Host.CapturePointer(args.Pointer);
        PaintHost(_over, pressed: false);
        _reorder.Lift(_id, Host, _pressedAt, args);
    }

    /// <summary>Ends a drag that lost the pointer. The item goes back to its place.</summary>
    private void EndDrag()
    {
        // A pinned tray icon is dragged without the item being lifted, so it ends here too, or the next press would still count as its drag.
        var lifted = _dragging;
        if (!lifted && !_draggingTray && !_armedTray)
        {
            return;
        }

        _dragging = false;
        _draggingTray = false;
        _armedTray = false;
        _trayKey = "";
        if (lifted)
        {
            _reorder?.Cancel();
        }

        PaintHost(_over, pressed: false);
    }

    /// <summary>Sends the plugin its left click, with the meter the press was on. The plugin opens its panel.</summary>
    private void LeftClick() => _gesture?.Invoke(WidgetGesture.Left, _hit);

    /// <summary>True when <paramref name="point"/> is inside this slot.</summary>
    private bool InsideHost(Point point) =>
        point.X >= 0 && point.Y >= 0 && point.X < Host.ActualWidth && point.Y < Host.ActualHeight;

    /// <summary>True when the press landed on a button inside the slot, such as a pinned tray icon.</summary>
    private bool PressedOnChildButton(object source)
    {
        var node = source as DependencyObject;
        while (node is not null && !ReferenceEquals(node, Host))
        {
            if (node is Button)
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private void PaintHost(bool over, bool pressed)
    {
        // A held item shows its whole outline, chips and pinned icons included.
        if (_dragging)
        {
            Host.Background = Paint.Hover;
            return;
        }

        if (_button.OwnHits)
        {
            Host.Background = Paint.Hit;
            return;
        }

        Host.Background = _active || (_button.TrayBare && _button.TrayOpen)
            ? Paint.Selected
            : pressed ? Paint.Pressed : over ? Paint.Hover : Paint.Hit;
    }

    /// <summary>True when this item's panel is open and the press is on the control that opened it.</summary>
    private bool BlockPress(Func<Point> pointInChevron)
    {
        if (!string.Equals(_session.OpenId, _id, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_button.Tray || _button.TrayBare)
        {
            return true;
        }

        return _button.InsideChevron(pointInChevron());
    }
}

/// <summary>
/// A bar slot the accessibility tree can invoke.
/// A posted mouse click never reaches WinUI, so a driver click has to find invoke on the slot.
/// </summary>
internal sealed class BarHit : ContentControl
{
    private static readonly ControlTemplate Shell = (ControlTemplate)XamlReader.Load(
        """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContentControl">
            <Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="{TemplateBinding CornerRadius}">
                <Border.BackgroundTransition>
                    <BrushTransition Duration="0:0:0.083" />
                </Border.BackgroundTransition>
                <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Stretch" />
            </Border>
        </ControlTemplate>
        """);

    public BarHit()
    {
        Template = Shell;
        IsTabStop = false;
    }

    /// <summary>Raised when the accessibility tree invokes this slot.</summary>
    public event TypedEventHandler<BarHit, RoutedEventArgs>? Invoked;

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(BarHit owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override bool IsControlElementCore() => true;

        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPatternCore(patternInterface);

        public void Invoke()
        {
            if (owner.DispatcherQueue.HasThreadAccess)
            {
                owner.Invoked?.Invoke(owner, new RoutedEventArgs());
                return;
            }

            owner.DispatcherQueue.TryEnqueue(() => owner.Invoked?.Invoke(owner, new RoutedEventArgs()));
        }
    }
}

