using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Winshell;

internal sealed partial class PanelWindow
{
    private readonly Grid _lane = new();
    private PanelOrigin _rest;
    private PanelOrigin _from;
    private PanelOrigin _to;
    private int _specWidth = -1;
    private int _specHeight = -1;
    private long _started;
    private TimeSpan _duration;
    private bool _sliding;
    private bool _hideWhenDone;
    private Action? _leaveDone;
    private EventHandler<object>? _onFrame;
    private EventHandler<object>? _onDrawFrame;
    private Action? _drawn;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _drawTimer;
    private int _drawnFrames;
    private bool _veiled;

    // Windows takes a drawn frame one refresh later, so one frame at the fitted size is not yet on the screen.
    private const int FramesToDraw = 2;
    private static readonly TimeSpan DrawWait = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Places the panel where its entrance starts, off the screen. Windows shows a window before it has drawn its new rows,
    /// as a black box, so the panel draws unseen until <see cref="IPanelWindow.Reveal"/>.
    /// </summary>
    private void Veil()
    {
        _veiled = true;
        PanelVeil.Cover(WinRT.Interop.WindowNative.GetWindowHandle(this));

        // The rows draw clear, so the fade starts from what is already there. Rows drawn whole would show for a frame before it.
        ElementCompositionPreview.GetElementVisual(_frame).Opacity = 0;
        Seat(Parked(Motion.Enabled()));
    }

    void IPanelWindow.WhenDrawn(Action drawn)
    {
        _drawn = drawn;
        _drawnFrames = 0;
        _onDrawFrame ??= OnDrawFrame;
        CompositionTarget.Rendering -= _onDrawFrame;
        CompositionTarget.Rendering += _onDrawFrame;

        // A window that draws no frame must not stay unseen. One timer for every open, stopped once the panel has drawn.
        if (_drawTimer is null)
        {
            _drawTimer = DispatcherQueue.CreateTimer();
            _drawTimer.Interval = DrawWait;
            _drawTimer.IsRepeating = false;
            _drawTimer.Tick += (timer, _) =>
            {
                timer.Stop();
                if (_drawn is null)
                {
                    return;
                }

                Log.Warning("Panel", "the panel drew no frame in " + DrawWait.TotalMilliseconds + " ms; showing it as it is. fitted " + _fitWidth + "x" + _fitHeight
                    + " window " + AppWindow.Size.Width + "x" + AppWindow.Size.Height + " client " + AppWindow.ClientSize.Width + "x" + AppWindow.ClientSize.Height
                    + " rows " + _lane.ActualWidth + "x" + _lane.ActualHeight + " scale " + _lane.XamlRoot?.RasterizationScale);
                Drawn();
            };
        }

        // Stopped first, so the wait counts from this open.
        _drawTimer.Stop();
        _drawTimer.Start();
    }

    void IPanelWindow.Reveal()
    {
        if (!_veiled)
        {
            return;
        }

        _veiled = false;
        BeginEnter();
        PanelVeil.Lift(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private void OnDrawFrame(object? sender, object args)
    {
        // This runs before each frame is drawn, so rows already laid out to the fitted size were drawn by the frame before.
        if (!LaidOutToFit())
        {
            _drawnFrames = 0;
            return;
        }

        if (++_drawnFrames >= FramesToDraw)
        {
            Drawn();
        }
    }

    /// <summary>True once the rows have been laid out in a window of the fitted size.</summary>
    private bool LaidOutToFit()
    {
        if (_fitWidth <= 0 || _fitHeight <= 0 || _lane.XamlRoot is not { } root)
        {
            return false;
        }

        // The window's outline is outside its client area, so the rows fill the client area and not the fitted size.
        var client = AppWindow.ClientSize;
        var scale = root.RasterizationScale;
        return AppWindow.Size.Width == _fitWidth
            && AppWindow.Size.Height == _fitHeight
            && Math.Abs((_lane.ActualWidth * scale) - client.Width) <= 1
            && Math.Abs((_lane.ActualHeight * scale) - client.Height) <= 1;
    }

    private void Drawn()
    {
        var drawn = _drawn;
        ForgetDrawn();
        drawn?.Invoke();
    }

    private void ForgetDrawn()
    {
        _drawn = null;
        _drawTimer?.Stop();
        if (_onDrawFrame is not null)
        {
            CompositionTarget.Rendering -= _onDrawFrame;
        }
    }

    /// <summary>Parks the panel under the bar and slides that whole window out.</summary>
    private void BeginEnter()
    {
        var from = Parked(Motion.Enabled());
        Seat(from);
        if (from.X == _rest.X && from.Y == _rest.Y)
        {
            Motion.FadeIn(_frame, TimeSpan.Zero);
            return;
        }

        Play(from, _rest, PanelMotion.EnterDuration, hideWhenDone: false);
        Motion.FadeIn(_frame, PanelMotion.EnterDuration);
    }

    /// <summary>Slides the panel back under the bar, then runs <paramref name="finished"/>. False when it should disappear at once.</summary>
    private bool BeginLeave(Action finished)
    {
        // A panel nobody saw arrive has nowhere to slide from.
        if (!_ready || _veiled || !AppWindow.IsVisible || !Motion.Enabled())
        {
            return false;
        }

        var position = AppWindow.Position;
        var from = new PanelOrigin(position.X, position.Y);
        var to = Parked(true);
        if (from.X == to.X && from.Y == to.Y)
        {
            return false;
        }

        Play(from, to, PanelMotion.LeaveDuration, hideWhenDone: true);
        _leaveDone = finished;
        return true;
    }

    /// <summary>Where the panel window sits. Tucked means one bar-thickness toward the bar, still the card's own size.</summary>
    private PanelOrigin Parked(bool tucked)
    {
        // A panel at the middle of the screen has no bar to slide from. It is there at once, and gone at once.
        if (!tucked || _ownerHwnd == 0 || _middle)
        {
            return _rest;
        }

        var shift = PanelMotion.TowardBar(_edge, motion: true);
        var travel = ScreenMetrics.Dips(_ownerHwnd, PanelMotion.Travel);
        var x = shift.X == 0 ? 0 : Math.Sign(shift.X) * travel;
        var y = shift.Y == 0 ? 0 : Math.Sign(shift.Y) * travel;
        return new PanelOrigin(_rest.X + x, _rest.Y + y);
    }

    /// <summary>The window is the panel. It is never a larger empty plate for the card to slide into.</summary>
    private void Seat(PanelOrigin origin)
    {
        KeepUnderBar();
        if (_fitWidth > 0 && _fitHeight > 0)
        {
            AppWindow.MoveAndResize(new RectInt32(origin.X, origin.Y, _fitWidth, _fitHeight));
            PanelBackfill.Paint(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }

        _lane.Padding = default;
    }

    private void PrepareLane()
    {
        _frame.HorizontalAlignment = HorizontalAlignment.Stretch;
        _frame.VerticalAlignment = VerticalAlignment.Stretch;
        _frame.IsTabStop = true;
        _lane.Children.Add(_frame);
        Content = _lane;
    }

    /// <summary>Moves the panel once per screen refresh, from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private void Play(PanelOrigin from, PanelOrigin to, TimeSpan duration, bool hideWhenDone)
    {
        _from = from;
        _to = to;
        _duration = duration;
        _hideWhenDone = hideWhenDone;
        _sliding = true;
        _started = Stopwatch.GetTimestamp();
        KeepUnderBar();
        AppWindow.Move(new PointInt32(from.X, from.Y));
        _onFrame ??= OnFrame;
        CompositionTarget.Rendering -= _onFrame;
        CompositionTarget.Rendering += _onFrame;
    }

    private void OnFrame(object? sender, object args)
    {
        if (!_sliding)
        {
            Disarm();
            return;
        }

        var point = SlideNow(out var done);
        AppWindow.Move(new PointInt32(point.X, point.Y));
        if (done)
        {
            CompleteSlide();
        }
    }

    /// <summary>Where the slide has the panel now. <paramref name="done"/> is true once its time is up, and the point is then its end exactly.</summary>
    private PanelOrigin SlideNow(out bool done)
    {
        var span = _duration.TotalMilliseconds;
        var time = span <= 0 ? 1 : Math.Clamp(Stopwatch.GetElapsedTime(_started).TotalMilliseconds / span, 0, 1);
        var progress = _hideWhenDone ? PanelMotion.LeaveProgress(time) : PanelMotion.EnterProgress(time);
        done = time >= 1;
        return PanelMotion.Along(_from, _to, progress);
    }

    private void StopSlide()
    {
        _sliding = false;
        _hideWhenDone = false;
        // A stopped leave never reports its end. The session starts the next one.
        _leaveDone = null;
        Disarm();
    }

    private void CompleteSlide()
    {
        var hide = _hideWhenDone;
        var done = _leaveDone;
        _sliding = false;
        _hideWhenDone = false;
        _leaveDone = null;
        Disarm();
        if (hide)
        {
            FinishHide();
            done?.Invoke();
        }
    }

    private void Disarm()
    {
        if (_onFrame is not null)
        {
            CompositionTarget.Rendering -= _onFrame;
        }
    }

    /// <summary>Seats a resting panel. An entrance that is still running keeps sliding toward the new place, at the new size.</summary>
    private void Settle()
    {
        if (_sliding && !_hideWhenDone)
        {
            // Only the place was passed on before, so a panel that grew while it slid in kept its first size and scrolled.
            // The start moves with the end. Left at the old place, a panel that grew beside a bottom bar slid in from under
            // where it used to rest, and sat past its new place by a share of what it grew until the slide ended.
            _from = Parked(true);
            _to = _rest;
            if (_fitWidth > 0 && _fitHeight > 0)
            {
                // Moved and sized in one call. Sized alone, the panel was on screen for a frame at its new size in its old place.
                var point = SlideNow(out _);
                AppWindow.MoveAndResize(new RectInt32(point.X, point.Y, _fitWidth, _fitHeight));
                PanelBackfill.Paint(WinRT.Interop.WindowNative.GetWindowHandle(this));
            }

            return;
        }

        if (_sliding)
        {
            return;
        }

        Seat(_rest);
    }

    private void FinishHide()
    {
        StopSlide();
        _fitWidth = -1;
        _fitHeight = -1;
        _specWidth = -1;
        _specHeight = -1;
        _lane.Padding = default;
        ForgetDrawn();
        AppWindow.Hide();
        if (_veiled)
        {
            _veiled = false;
            PanelVeil.Lift(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }

        // A hidden panel draws its rows again when it opens, so it keeps none of their pictures meanwhile.
        _rows.Children.Clear();

        // Nor the acrylic controller, which is connected for as long as the backdrop is set. Every open sets it again (ApplyChrome).
        try
        {
            SystemBackdrop = null;
        }
        catch (Exception ex)
        {
            Log.Warning("Theme", "the hidden panel kept its backdrop", ex);
        }
    }

    private void KeepUnderBar()
    {
        var panel = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (_middle)
        {
            // The bar may be behind a window that fills the screen. A panel at the middle stays in front of that window.
            PanelLayer.Pin(panel, 0);
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && !presenter.IsAlwaysOnTop)
            {
                presenter.IsAlwaysOnTop = true;
            }

            return;
        }

        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter beside && beside.IsAlwaysOnTop != _above)
        {
            beside.IsAlwaysOnTop = _above;
        }

        PanelLayer.Pin(panel, _ownerHwnd);
        BorderlessFrame.Under(panel, _ownerHwnd);
    }
}

