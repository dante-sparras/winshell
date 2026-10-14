using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Winshell;

/// <summary>
/// One monitor's copy of a toast, at that monitor's Bar's corner. The window slides in from past the screen edge and slides back off it
/// when it is sent away. It never takes focus. <see cref="NotificationToasts"/> owns the toast: it keeps the toast's time, places this
/// copy in its monitor's stack, and sends every copy away together.
/// </summary>
internal sealed class ToastWindow : Window
{
    /// <summary>Toast width, in device-independent pixels. Windows draws its toasts this wide.</summary>
    public const int WidthDips = 364;

    private readonly ShellBackdrop _backdrop = new();
    private readonly Border _frame;
    private bool _prepared;
    private bool _shown;
    private bool _leaving;
    private bool _over;
    private int _generation;
    private Action<string>? _send;
    private PixelRect _slot;
    private int _travel;
    private int _offset;
    private int _slideFrom;
    private bool _sliding;
    private bool _slideArriving;
    private long _slideStart;
    private TimeSpan _slideLength;
    private Action? _slideDone;
    private int _lift;
    private int _restackFrom;
    private long _restackStart;
    private bool _restacking;

    /// <summary>Creates a hidden toast window.</summary>
    public ToastWindow()
    {
        _frame = new Border
        {
            Padding = Spacing.Toast,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetLiveSetting(_frame, AutomationLiveSetting.Polite);
        _frame.PointerEntered += (_, _) =>
        {
            _over = true;
            Hovered?.Invoke(this);
        };
        _frame.PointerExited += (_, _) =>
        {
            _over = false;
            Hovered?.Invoke(this);
        };
        Content = _frame;
        Title = "Winoma toast";
    }

    /// <summary>Height in physical pixels, once <see cref="Fill"/> has measured it.</summary>
    public int HeightPx { get; private set; }

    /// <summary>Width in physical pixels.</summary>
    public int WidthPx { get; private set; }

    /// <summary>True from the first frame on screen until it has slid out and hidden. A copy that is not on screen has nothing to slide out.</summary>
    public bool OnScreen => _shown;

    /// <summary>True while the pointer rests on this copy.</summary>
    public bool Over => _over;

    /// <summary>It has slid out and hidden. The stack can close the gap and reuse it.</summary>
    public event Action<ToastWindow>? Gone;

    /// <summary>The pointer came to rest on this copy or left it. <see cref="Over"/> says which.</summary>
    public event Action<ToastWindow>? Hovered;

    /// <summary>Lays out <paramref name="face"/> and measures it for a monitor at <paramref name="scale"/>.</summary>
    /// <param name="face">What it shows.</param>
    /// <param name="scale">Physical pixels per device-independent pixel.</param>
    /// <param name="send">Sends a command to the toast's plugin.</param>
    public void Fill(ToastFace face, double scale, Action<string> send)
    {
        _send = send;
        _frame.Child = NotificationCards.Card(face.Card, Send, framed: false);
        AutomationProperties.SetName(_frame, face.Card.App + ", " + face.Card.Title);
        var inner = WidthDips - _frame.Padding.Left - _frame.Padding.Right;
        _frame.Child.Measure(new Windows.Foundation.Size(inner, double.PositiveInfinity));
        WidthPx = (int)Math.Ceiling(WidthDips * scale);
        HeightPx = ToastPlace.Height(_frame.Child.DesiredSize.Height + _frame.Padding.Top + _frame.Padding.Bottom, scale);
    }

    /// <summary>
    /// Puts it at a physical rectangle. The first time, the window slides in from past the screen edge.
    /// A move while it slides changes where the slide ends. At rest, a move along the stack is eased.
    /// </summary>
    /// <param name="slot">Where it rests.</param>
    /// <param name="travel">How far right of <paramref name="slot"/> it is off the screen, in pixels.</param>
    public void Place(PixelRect slot, int travel)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!_prepared)
        {
            var presenter = OverlappedPresenter.CreateForContextMenu();
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            AppWindow.SetPresenter(presenter);
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            AppWindow.TitleBar.SetDragRectangles([]);
            AppWindow.IsShownInSwitchers = false;
            _prepared = true;
        }

        var was = _slot;
        var resized = slot.Width != was.Width || slot.Height != was.Height;
        _slot = slot;
        _travel = travel;
        var arriving = !_shown;
        if (!arriving)
        {
            // A toast at rest whose slot moved along the stack eases there, and one already on its way goes on from where it is drawn.
            // A new size, another column, or Windows animation effects off puts it on the slot at once.
            var along = !resized && slot.X == was.X && slot.Y != was.Y;
            if (along && (_restacking || !_sliding) && Motion.Enabled())
            {
                Restack(was.Y + _lift);
            }
            else if (slot != was)
            {
                EndRestack();
            }

            // A slide or a restack in progress picks up the new place on its next frame. A frame only moves the window, so a new size is set here.
            if (!_sliding && !_restacking)
            {
                FloatingPill.ShowAt(hwnd, slot.X, slot.Y, slot.Width, slot.Height);
            }
            else if (resized)
            {
                FloatingPill.ShowAt(hwnd, slot.X + _offset, slot.Y, slot.Width, slot.Height);
            }

            return;
        }

        ApplyChrome(hwnd);
        var start = Motion.Enabled() ? slot.X + travel : slot.X;
        _offset = start - slot.X;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(start, slot.Y, slot.Width, slot.Height));
        AppWindow.Show(false);
        FloatingPill.ShowAt(hwnd, start, slot.Y, slot.Width, slot.Height);
        _shown = true;
        _leaving = false;
        _generation++;
        Slide(arriving: true, done: null);
        if (FrameworkElementAutomationPeer.FromElement(_frame) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    /// <summary>Slides it off the screen edge, then hides it and raises <see cref="Gone"/>.</summary>
    public void Leave()
    {
        if (!_shown || _leaving)
        {
            return;
        }

        _leaving = true;
        var generation = _generation;
        Slide(arriving: false, done: () =>
        {
            if (generation != _generation || !_leaving)
            {
                return;
            }

            _shown = false;
            _leaving = false;
            _over = false;
            AppWindow.Hide();
            EndRestack();

            // A spare window keeps nothing of the notification it showed: not its card and pictures, not the way back to
            // its plugin, and not the acrylic controller. The next arrival fills the card and sets the backdrop again (Place).
            _frame.Child = null;
            _send = null;
            try
            {
                SystemBackdrop = null;
            }
            catch (Exception ex)
            {
                Log.Warning("Theme", "the hidden toast kept its backdrop", ex);
            }

            Gone?.Invoke(this);
        });
    }

    private void Send(string command)
    {
        _send?.Invoke(command);
    }

    /// <summary>
    /// Moves the whole window, backdrop and all, one frame at a time, so the toast leaves the way it came.
    /// With Windows animation effects off, it lands at once.
    /// </summary>
    private void Slide(bool arriving, Action? done)
    {
        // A toast sent away while it slides in leaves from where it was last drawn. From its resting place it would jump there first.
        _slideFrom = arriving ? 0 : _offset;
        _slideArriving = arriving;
        _slideDone = done;
        _slideStart = Stopwatch.GetTimestamp();
        _slideLength = UiMotion.Slow;
        if (!Motion.Enabled())
        {
            Frame(1);
            return;
        }

        if (!_sliding)
        {
            _sliding = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void OnFrame(object? sender, object args)
    {
        var elapsed = Stopwatch.GetElapsedTime(_slideStart).TotalMilliseconds;
        Frame(Math.Clamp(elapsed / _slideLength.TotalMilliseconds, 0, 1));
    }

    private void Frame(double time)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var from = Math.Min(_slideFrom, _travel);
        _offset = _slideArriving
            ? ToastPlace.SlideOffset(_travel, time, arriving: true)
            : from + ToastPlace.SlideOffset(_travel - from, time, arriving: false);
        FloatingPill.MoveTo(hwnd, _slot.X + _offset, _slot.Y + _lift);
        if (time < 1)
        {
            return;
        }

        if (_sliding)
        {
            _sliding = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        var done = _slideDone;
        _slideDone = null;
        done?.Invoke();
    }

    /// <summary>
    /// Eases the window along the stack to its slot from <paramref name="from"/>, where it is drawn now, so a gap closes by a slide.
    /// A restack already on its way starts its time again from there. The rule is <see cref="ToastPlace.RestackOffset"/>.
    /// </summary>
    private void Restack(int from)
    {
        _restackFrom = from;
        _restackStart = Stopwatch.GetTimestamp();
        _lift = from - _slot.Y;
        if (!_restacking)
        {
            _restacking = true;
            CompositionTarget.Rendering += OnRestackFrame;
        }
    }

    private void OnRestackFrame(object? sender, object args)
    {
        // A toast that hid on this frame has ended its restack, and the frame may still be handed here once.
        if (!_restacking)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_restackStart);
        _lift = ToastPlace.RestackOffset(_restackFrom, _slot.Y, elapsed);
        FloatingPill.MoveTo(WinRT.Interop.WindowNative.GetWindowHandle(this), _slot.X + _offset, _slot.Y + _lift);
        if (elapsed >= ToastPlace.RestackLength)
        {
            EndRestack();
        }
    }

    /// <summary>Puts the window's place along the stack back on its slot and stops asking for frames. The caller moves the window.</summary>
    private void EndRestack()
    {
        _lift = 0;
        if (_restacking)
        {
            _restacking = false;
            CompositionTarget.Rendering -= OnRestackFrame;
        }
    }

    private void ApplyChrome(nint hwnd)
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
                if (SystemBackdrop != _backdrop)
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
            Log.Warning("Theme", "toast acrylic failed", ex);
        }

        _frame.Background = acrylic
            ? Paint.Hit
            : Paint.Background;
        FloatingPill.Prepare(hwnd, chrome.Light);
        BorderlessFrame.Round(hwnd, chrome.Light, outline: true);
    }
}

