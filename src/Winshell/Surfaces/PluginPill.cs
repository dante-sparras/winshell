using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Winshell;

/// <summary>
/// The floating pill a plugin shows with <see cref="IPluginContext.ShowPill"/>, at the bottom of the screen.
/// Its window is made the first time a plugin shows a face, so a session in which nothing asks for the pill has none.
/// It is then kept between takes: the first frame of a take does not wait on a window.
/// </summary>
internal sealed class PluginPill
{
    private readonly Action<string, string> _send;
    private readonly Func<BarEdge?> _bottomBar;
    private PluginPillWindow? _window;

    /// <summary>Creates the pill without its window.</summary>
    /// <param name="send">Sends a plugin command: the owner's id, then the command a button names.</param>
    /// <param name="bottomBar">The bar edge when the bar is visible, so the pill can sit above a bottom bar.</param>
    public PluginPill(Action<string, string> send, Func<BarEdge?> bottomBar)
    {
        _send = send;
        _bottomBar = bottomBar;
    }

    /// <summary>Adds an input level from <paramref name="owner"/>. A plugin that does not own the pill is ignored. Safe to call from any thread.</summary>
    public void Feed(string owner, float level) => Volatile.Read(ref _window)?.Feed(owner, level);

    /// <summary>Shows <paramref name="face"/> for <paramref name="owner"/>. Call on the UI thread.</summary>
    /// <param name="owner">The plugin the buttons send to.</param>
    /// <param name="face">What to draw. A hidden face from a plugin that no longer owns the pill does nothing.</param>
    public void Show(string owner, PillFace face)
    {
        var window = _window;
        if (window is null)
        {
            // Nothing was shown yet, so there is nothing to hide.
            if (face.State == PillState.Hidden)
            {
                return;
            }

            window = new PluginPillWindow(_send, _bottomBar);
            Volatile.Write(ref _window, window);
        }

        window.Show(owner, face);
    }

    /// <summary>Hides the pill when the plugin that owns it is no longer running. Call on the UI thread.</summary>
    /// <param name="running">True for a plugin id that is still running.</param>
    public void Forget(Func<string, bool> running) => _window?.Forget(running);

    /// <summary>Closes the window, when one was made. The desktop is leaving.</summary>
    public void Close() => _window?.Close();
}

/// <summary>
/// The pill's window. Live, it draws the input level and the elapsed time, and can offer cancel and stop.
/// Then it shows progress, and a check or the reason the work failed. It never takes focus.
/// One plugin owns it at a time: the latest face wins.
/// </summary>
internal sealed class PluginPillWindow : Window
{
    private const int Bars = 20;
    private const double BarWidth = 3;
    private const double BarGap = 3;
    private const double LowBar = 3;
    private const double HighBar = 22;
    private const int HeightDips = 44;
    private const double Band = 28;
    private const double BandTop = (HeightDips - Band) / 2;
    private const double Gap = 10;
    private const double Round = 28;
    private const double StopGlyph = 10;
    private const int LiftDips = 28;

    /// <summary>A bar this near its target is drawn at it, since heights are drawn to the half pixel.</summary>
    private const double Settled = 0.25;
    private static readonly TimeSpan Sample = TimeSpan.FromMilliseconds(45);
    private static readonly TimeSpan InsertedFor = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan FailedFor = TimeSpan.FromMilliseconds(2800);

    private readonly Rectangle[] _bars = new Rectangle[Bars];
    private readonly ScaleTransform[] _scale = new ScaleTransform[Bars];
    private readonly double[] _target = new double[Bars];
    private readonly double[] _drawn = new double[Bars];
    private readonly double[] _written = new double[Bars];
    private readonly float[] _history = new float[Bars];
    private readonly object _levelGate = new();
    private readonly ShellBackdrop _backdrop = new();
    private readonly Grid _frame;
    private readonly StackPanel _row;
    private readonly FontIcon _glyph;
    private readonly ProgressRing _ring;
    private readonly Button _cancel;
    private readonly Button _stop;
    private readonly TextBlock _clock;
    private readonly TextBlock _message;
    private readonly Grid _wave;
    private readonly DispatcherTimer _hide;
    private readonly Func<BarEdge?> _bottomBar;
    private readonly Action<string, string> _send;
    private PillFace _face = PillFace.Hidden;
    private string _owner = "";
    private float _peak;
    private long _listeningSince;
    private long _lastSample;
    private long _lastFrame;
    private double _phase;
    private int _shownSeconds = -1;
    private bool _prepared;
    private bool _shown;
    private bool _rendering;
    private bool _motion = true;

    /// <summary>Creates the hidden pill.</summary>
    /// <param name="send">Sends a plugin command: the owner's id, then the command a button names.</param>
    /// <param name="bottomBar">The bar edge when the bar is visible, so the pill can sit above a bottom bar.</param>
    public PluginPillWindow(Action<string, string> send, Func<BarEdge?> bottomBar)
    {
        _send = send;
        _bottomBar = bottomBar;
        // An unsized icon box grows to whatever height it is measured at, and the
        // mic then sits at the top of that box. 16 by 16 is the glyph, nothing else.
        _glyph = new FontIcon
        {
            FontFamily = Fonts.Icons,
            FontSize = TypeRamp.Icon,
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        _ring = new ProgressRing { Width = 16, Height = 16, IsActive = false, Visibility = Visibility.Collapsed };
        _cancel = RoundButton("", "Cancel", accent: false);
        _cancel.Click += (_, _) => Press(_face.Cancel);
        _stop = RoundButton("", "Stop", accent: true);
        _stop.Click += (_, _) => Press(_face.Stop);
        _clock = new TextBlock
        {
            Style = Winshell.Kit.Paint.Style("BarTextStyle"),
            Foreground = Winshell.Kit.Paint.Muted,
            FontSize = TypeRamp.Caption,
            MinWidth = 30,
            TextAlignment = TextAlignment.Right,
        };
        _message = new TextBlock
        {
            Style = Winshell.Kit.Paint.Style("BarTextStyle"),
            Foreground = Winshell.Kit.Paint.Foreground,
            FontSize = TypeRamp.Body,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
        };
        // The bars animate inside a slot as tall as the loudest one. Growing a bar
        // used to make the row taller, and that second measure is what moved the
        // mic, the bars, and the timer into the middle after speech started.
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = BarGap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _wave = new Grid
        {
            Height = HighBar,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _wave.Children.Add(bars);
        for (var i = 0; i < Bars; i++)
        {
            var scale = new ScaleTransform { ScaleX = 1, ScaleY = LowBar / HighBar };
            _scale[i] = scale;
            _bars[i] = new Rectangle
            {
                Width = BarWidth,
                Height = HighBar,
                RadiusX = BarWidth / 2,
                RadiusY = BarWidth / 2,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 1),
                RenderTransform = scale,
                Fill = Winshell.Kit.Paint.Foreground,
            };
            _target[i] = LowBar;
            _drawn[i] = LowBar;
            _written[i] = LowBar;
            bars.Children.Add(_bars[i]);
        }

        // The cancel button, the mic, and the ring share one slot. A fixed 28-dip box
        // left the mic sitting in a hole, away from the bars, whenever cancel was hidden.
        var lead = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _ring.HorizontalAlignment = HorizontalAlignment.Center;
        _ring.VerticalAlignment = VerticalAlignment.Center;
        _message.VerticalAlignment = VerticalAlignment.Center;
        _clock.VerticalAlignment = VerticalAlignment.Center;
        lead.Children.Add(_glyph);
        lead.Children.Add(_ring);
        lead.Children.Add(_cancel);
        // Spacing follows visible children. A grid column kept a gap after the timer
        // for the stop button even while that button was collapsed.
        // The band is the stop button's height. Its top margin is half the slack in
        // the 44-dip pill, so the first arrange cannot pin the row to the top.
        // Centered alignment was dropped on that pass and only came back once a
        // growing bar changed the row's height, which is why speech appeared to fix it.
        _row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Gap,
            Height = Band,
            Margin = new Thickness(0, BandTop, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _row.Children.Add(lead);
        _row.Children.Add(_wave);
        _row.Children.Add(_message);
        _row.Children.Add(_clock);
        _row.Children.Add(_stop);
        _frame = new Grid
        {
            Height = HeightDips,
            Padding = new Thickness(Spacing.PopupPadding, 0, Spacing.PopupPadding, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _frame.Children.Add(_row);
        AutomationProperties.SetLiveSetting(_frame, AutomationLiveSetting.Polite);
        Content = _frame;
        Title = "Winoma pill";
        _hide = new DispatcherTimer();
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            HideNow();
        };
    }

    /// <summary>Adds an input level from <paramref name="owner"/>. A plugin that does not own the pill is ignored. Safe to call from any thread.</summary>
    public void Feed(string owner, float level)
    {
        // A level that is not a number would stay in every bar it passed through until the take ends.
        if (float.IsNaN(level) || !owner.Equals(Volatile.Read(ref _owner), StringComparison.Ordinal))
        {
            return;
        }

        // A level is from 0 to 1. A higher one would draw a bar taller than its slot.
        lock (_levelGate)
        {
            _peak = Math.Max(_peak, Math.Min(level, 1f));
        }
    }

    /// <summary>Shows <paramref name="face"/> for <paramref name="owner"/>. Call on the UI thread.</summary>
    /// <param name="owner">The plugin the buttons send to.</param>
    /// <param name="face">What to draw. A hidden face from a plugin that no longer owns the pill does nothing.</param>
    public void Show(string owner, PillFace face)
    {
        if (face.State == PillState.Hidden && !owner.Equals(_owner, StringComparison.Ordinal))
        {
            return;
        }

        var previous = _owner.Equals(owner, StringComparison.Ordinal) ? _face : PillFace.Hidden;
        Volatile.Write(ref _owner, owner);
        _face = face;
        _hide.Stop();
        switch (face.State)
        {
            case PillState.Hidden:
                HideNow();
                return;
            case PillState.Live:
                if (previous.State != PillState.Live)
                {
                    _listeningSince = Stopwatch.GetTimestamp();
                    _shownSeconds = -1;
                    _clock.Text = "0:00";
                    Array.Clear(_history);
                    Array.Fill(_target, LowBar);
                    Array.Fill(_drawn, LowBar);
                    for (var i = 0; i < Bars; i++)
                    {
                        Write(i, LowBar);
                    }

                    lock (_levelGate)
                    {
                        _peak = 0;
                    }
                }

                break;
            case PillState.Done:
                HideAfter(InsertedFor);
                break;
            case PillState.Failed:
                HideAfter(FailedFor);
                break;
        }

        Paint();
        Present();
    }

    /// <summary>Hides the pill when the plugin that owns it is no longer running. Call on the UI thread.</summary>
    /// <param name="running">True for a plugin id that is still running.</param>
    public void Forget(Func<string, bool> running)
    {
        // A stopped plugin sends no hidden face, so a live or working pill would stay up and ask for a frame every refresh for good.
        if (_face.State != PillState.Hidden && !running(_owner))
        {
            Show(_owner, PillFace.Hidden);
        }
    }

    private void Press(string command)
    {
        if (command.Length > 0 && _owner.Length > 0)
        {
            _send(_owner, command);
        }
    }

    private void Paint()
    {
        var state = _face.State;
        var live = state == PillState.Live;
        var controls = live && _face.Controls;
        _cancel.Visibility = controls && _face.Cancel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _stop.Visibility = controls && _face.Stop.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _ring.IsActive = state == PillState.Working;
        _ring.Visibility = state == PillState.Working ? Visibility.Visible : Visibility.Collapsed;
        _glyph.Visibility = (live && !controls) || state is PillState.Done or PillState.Failed
            ? Visibility.Visible
            : Visibility.Collapsed;
        _glyph.Glyph = state switch
        {
            PillState.Done => "",
            PillState.Failed => "",
            _ => Glyphs.Named(_face.Glyph) is { Length: > 0 } named ? named : "",
        };
        _glyph.Foreground = state == PillState.Failed
            ? Winshell.Kit.Paint.Fill(PanelTone.Warn)
            : Winshell.Kit.Paint.AccentText;
        _clock.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        var failed = state == PillState.Failed;
        _wave.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
        _wave.Opacity = state == PillState.Working ? 0.55 : 1;
        _message.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        _message.Text = failed ? _face.Message : "";
        AutomationProperties.SetName(_frame, _face.Label.Length > 0 ? _face.Label : _owner);
        if (FrameworkElementAutomationPeer.FromElement(_frame) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        if (state == PillState.Done)
        {
            Array.Fill(_target, LowBar);
        }
    }

    private void Present()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!_prepared)
        {
            var presenter = OverlappedPresenter.CreateForContextMenu();
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            AppWindow.SetPresenter(presenter);
            // A standard caption band is most of this 44-dip window, and the row was laid out in that band.
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            AppWindow.TitleBar.SetDragRectangles([]);
            AppWindow.IsShownInSwitchers = false;
            _prepared = true;
        }

        var arriving = !_shown;
        if (arriving)
        {
            ApplyChrome(hwnd);
        }

        var (centerX, bottom, scale) = FloatingPill.Anchor();
        var dips = WidthDips();
        _frame.Width = dips;
        _frame.Height = HeightDips;
        _row.Margin = new Thickness(0, BandTop, 0, 0);
        var width = (int)Math.Ceiling(dips * scale);
        var height = (int)Math.Ceiling(HeightDips * scale);
        var lift = LiftDips + (_bottomBar() == BarEdge.Bottom ? BarPlacement.Horizontal : 0);
        var x = centerX - (width / 2);
        var y = bottom - height - (int)Math.Round(lift * scale);
        if (arriving)
        {
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
            AppWindow.Show(false);
        }

        FloatingPill.ShowAt(hwnd, x, y, width, height);
        _shown = true;
        _frame.UpdateLayout();
        if (arriving)
        {
            Enter();
        }

        if (!_rendering)
        {
            _rendering = true;
            _lastFrame = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private double WidthDips()
    {
        // The band's own height, not the window. A window-height measure stretched the icon.
        _row.Measure(new Windows.Foundation.Size(double.PositiveInfinity, Band));
        var width = _frame.Padding.Left + _row.DesiredSize.Width + _frame.Padding.Right;
        _row.InvalidateMeasure();
        _frame.InvalidateMeasure();
        return Math.Clamp(Math.Ceiling(width), 120, 460);
    }

    private void ApplyChrome(nint hwnd)
    {
        var chrome = Winshell.Kit.Paint.Chrome;
        _frame.RequestedTheme = chrome.HighContrast
            ? ElementTheme.Default
            : chrome.Light ? ElementTheme.Light : ElementTheme.Dark;
        _motion = Motion.Enabled();
        var acrylic = !chrome.HighContrast && chrome.Effects && ShellBackdrop.IsSupported;
        try
        {
            if (acrylic)
            {
                _backdrop.Configure(chrome.Light, chrome.Background, 0.78f, 0.94f);
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
            Log.Warning("Theme", "pill acrylic failed", ex);
        }

        _frame.Background = acrylic
            ? Winshell.Kit.Paint.Hit
            : Winshell.Kit.Paint.Background;
        FloatingPill.Prepare(hwnd, chrome.Light);
    }

    private void Enter()
    {
        if (!_motion)
        {
            return;
        }

        Motion.FadeIn(_row, UiMotion.Fast);
    }

    private void HideAfter(TimeSpan delay)
    {
        _hide.Interval = delay;
        _hide.Start();
    }

    private void HideNow()
    {
        if (_rendering)
        {
            CompositionTarget.Rendering -= OnFrame;
            _rendering = false;
        }

        if (!_shown)
        {
            return;
        }

        _shown = false;
        _ring.IsActive = false;
        AppWindow.Hide();

        // A hidden pill holds no acrylic controller, which is connected for as long as the backdrop is set.
        // Every arrival sets it again (ApplyChrome).
        try
        {
            SystemBackdrop = null;
        }
        catch (Exception ex)
        {
            Log.Warning("Theme", "the hidden pill kept its backdrop", ex);
        }
    }

    private void OnFrame(object? sender, object e)
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
        _lastFrame = now;
        switch (_face.State)
        {
            case PillState.Live:
                Listen(now);
                break;
            case PillState.Working:
                _phase += seconds * 7;
                for (var i = 0; i < Bars; i++)
                {
                    var swell = _motion ? 0.5 + (0.5 * Math.Sin(_phase - (i * 0.45))) : 0.3;
                    _target[i] = LowBar + ((HighBar - LowBar) * 0.28 * swell);
                }

                break;
        }

        var ease = _motion ? Math.Min(1, seconds * 18) : 1;
        var settled = true;
        for (var i = 0; i < Bars; i++)
        {
            _drawn[i] += (_target[i] - _drawn[i]) * ease;
            if (Math.Abs(_target[i] - _drawn[i]) >= Settled)
            {
                settled = false;
            }

            Write(i, Math.Round(_drawn[i] * 2) / 2);
        }

        // A check or a failure stays up with nothing moving, and so does work in progress while animation effects are off.
        // Once the bars are where they end, no more frames are asked for. The next face starts them again (Present).
        if (settled && (_face.State is PillState.Done or PillState.Failed || (!_motion && _face.State == PillState.Working)))
        {
            for (var i = 0; i < Bars; i++)
            {
                _drawn[i] = _target[i];
                Write(i, Math.Round(_drawn[i] * 2) / 2);
            }

            CompositionTarget.Rendering -= OnFrame;
            _rendering = false;
        }
    }

    /// <summary>Sets a bar's height when it differs from the one it has. Scale does not measure the pill again.</summary>
    private void Write(int bar, double height)
    {
        if (height == _written[bar])
        {
            return;
        }

        _written[bar] = height;
        _scale[bar].ScaleY = height / HighBar;
    }

    private void Listen(long now)
    {
        if (Stopwatch.GetElapsedTime(_lastSample, now) >= Sample)
        {
            _lastSample = now;
            float peak;
            lock (_levelGate)
            {
                peak = _peak;
                _peak = 0;
            }

            Array.Copy(_history, 1, _history, 0, Bars - 1);
            _history[^1] = peak;
            for (var i = 0; i < Bars; i++)
            {
                // A square root lifts quiet speech so the bars move for a normal voice, not only a loud one.
                _target[i] = LowBar + ((HighBar - LowBar) * Math.Sqrt(_history[i]));
            }
        }

        var elapsed = (int)Stopwatch.GetElapsedTime(_listeningSince, now).TotalSeconds;
        if (elapsed != _shownSeconds)
        {
            _shownSeconds = elapsed;
            _clock.Text = (elapsed / 60) + ":" + (elapsed % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static Button RoundButton(string glyph, string name, bool accent)
    {
        var button = new Button
        {
            Style = Winshell.Kit.Paint.Style("BarButtonStyle"),
            Width = Round,
            Height = Round,
            Padding = new Thickness(0),
            CornerRadius = Corners.Circle(Round),
            Content = new FontIcon { FontFamily = Fonts.Icons, FontSize = accent ? StopGlyph : TypeRamp.Caption, Glyph = glyph },
            Visibility = Visibility.Collapsed,
            IsTabStop = false,
        };
        if (accent)
        {
            button.Background = Winshell.Kit.Paint.Accent;
            button.Foreground = Winshell.Kit.Paint.OnAccent;
        }

        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }
}

