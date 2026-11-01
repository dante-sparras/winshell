using Microsoft.UI.Xaml;

namespace Winshell;

/// <summary>
/// A top-level surface of the shell. Set <see cref="Content"/> to any <see cref="UIElement"/>: XAML, or a
/// <c>ReactorHostControl</c>. Its background stays transparent, so <see cref="Backdrop"/> shows through.
/// </summary>
public abstract class ShellWindow : ObservableObject, IHostClient, IDisposable
{
    private readonly Shell _shell;
    private HostWindow? _host;
    private UIElement? _content;
    private bool _visible;
    private bool _active;
    private double _width = double.NaN;
    private double _height = double.NaN;
    private double _actualWidth;
    private double _actualHeight;
    private double _scale = 1;
    private double _opacity = 1;
    private ScreenInfo? _screen;
    private WindowBackdrop _backdrop;
    private WindowCorners _corners;
    private IReadOnlyList<QRect>? _inputRegion;
    private QRect? _visibleMask;
    private bool _disposed;

    private protected ShellWindow(Shell shell) => _shell = shell;

    private protected abstract WindowKind Kind { get; }

    internal HostWindow? Host => _host;

    internal Shell Shell => _shell;

    public UIElement? Content
    {
        get => _content;
        set
        {
            if (!Set(ref _content, value))
                return;
            if (_host is not null)
                _host.Content = value;
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_disposed || !Set(ref _visible, value))
                return;
            if (value)
                EnsureHost();
            if (_host is not null)
            {
                ApplyPlacement();
                _host.SetVisible(value);
            }
        }
    }

    /// <summary>Whether this window is the foreground window.</summary>
    public bool Active => _active;

    /// <summary>The width in logical pixels. NaN leaves it to the window's kind: anchors, or the default size.</summary>
    public double Width
    {
        get => _width;
        set
        {
            if (Set(ref _width, value))
                OnPlacementChanged();
        }
    }

    /// <summary>The height in logical pixels. NaN leaves it to the window's kind: anchors, or the default size.</summary>
    public double Height
    {
        get => _height;
        set
        {
            if (Set(ref _height, value))
                OnPlacementChanged();
        }
    }

    public double ActualWidth => _actualWidth;

    public double ActualHeight => _actualHeight;

    /// <summary>How many physical pixels this window draws for one logical pixel.</summary>
    public double Scale => _scale;

    /// <summary>The window's origin on the virtual desktop, in logical pixels.</summary>
    public QPoint Origin => _host?.Origin ?? default;

    /// <summary>The screen this window is on, or was asked to be on. Null is the primary screen.</summary>
    public ScreenInfo? Screen
    {
        get => _screen;
        set
        {
            if (Set(ref _screen, value))
                OnPlacementChanged();
        }
    }

    /// <summary>Fades the content. The material behind it does not fade.</summary>
    public double Opacity
    {
        get => _opacity;
        set
        {
            if (Set(ref _opacity, Math.Clamp(value, 0, 1)))
                _host?.SetOpacity(_opacity);
        }
    }

    public WindowBackdrop Backdrop
    {
        get => _backdrop;
        set
        {
            if (Set(ref _backdrop, value))
                _host?.SetBackdrop(_backdrop, _corners);
        }
    }

    public WindowCorners Corners
    {
        get => _corners;
        set
        {
            if (Set(ref _corners, value))
                _host?.SetBackdrop(_backdrop, _corners);
        }
    }

    /// <summary>
    /// The parts of the window that take clicks, in logical pixels; clicks elsewhere go to the window below. Null is
    /// the whole window. An empty list lets every click through.
    /// </summary>
    public IReadOnlyList<QRect>? InputRegion
    {
        get => _inputRegion;
        set
        {
            _inputRegion = value;
            Changed();
            _host?.SetInputRegion(value);
        }
    }

    /// <summary>Clips the content to a rectangle, in logical pixels. Null shows all of it.</summary>
    public QRect? VisibleMask
    {
        get => _visibleMask;
        set
        {
            if (Set(ref _visibleMask, value))
                _host?.SetVisibleMask(value);
        }
    }

    /// <summary>The user closed the window. It is hidden, and can be shown again.</summary>
    public event Action? Closed;

    /// <summary>The native window failed. It was rebuilt with the same content.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Takes the keyboard, if this kind of window may.</summary>
    public void Focus() => _host?.SetKeyboardFocus(true);

    public void Raise() => _host?.Raise();

    public void StartSystemMove() => _host?.StartSystemMove();

    public void StartSystemResize(Edges edges) => _host?.StartSystemResize(edges);

    /// <summary>A point in logical window coordinates, in physical pixels on the virtual desktop.</summary>
    public (int X, int Y) MapToScreen(double x, double y) => _host?.MapToScreen(x, y) ?? ((int)x, (int)y);

    private protected void EnsureHost()
    {
        if (_host is not null)
            return;
        _host = _shell.Platform.CreateWindow(Kind, this);
        _host.Content = _content;
        _host.SetBackdrop(_backdrop, _corners);
        if (_opacity != 1)
            _host.SetOpacity(_opacity);
        if (_inputRegion is not null)
            _host.SetInputRegion(_inputRegion);
        if (_visibleMask is { } mask)
            _host.SetVisibleMask(mask);
    }

    /// <summary>Tells the native window where to be. Only called with a host.</summary>
    private protected abstract void ApplyPlacement();

    private protected void OnPlacementChanged()
    {
        if (_host is not null)
            ApplyPlacement();
    }

    /// <summary>The window lost focus or was dismissed by a click elsewhere.</summary>
    private protected virtual void OnDismissed()
    {
    }

    private protected virtual void OnStateChanged(bool maximized, bool minimized)
    {
    }

    private protected virtual void OnFullscreenChanged(bool fullscreen)
    {
    }

    void IHostClient.OnResized(double width, double height, double scale)
    {
        Set(ref _actualWidth, width, nameof(ActualWidth));
        Set(ref _actualHeight, height, nameof(ActualHeight));
        Set(ref _scale, scale, nameof(Scale));
    }

    void IHostClient.OnMoved(double x, double y) => Changed(nameof(Origin));

    void IHostClient.OnClosed()
    {
        Visible = false;
        Closed?.Invoke();
    }

    void IHostClient.OnActiveChanged(bool active) => Set(ref _active, active, nameof(Active));

    void IHostClient.OnScreenChanged(string screenId)
    {
        var screen = _shell.Platform.Screens.FirstOrDefault(s => s.Id == screenId);
        if (screen is not null && screen != _screen && _screen is null)
            Changed(nameof(Screen));
    }

    void IHostClient.OnDismissed() => OnDismissed();

    void IHostClient.OnStateChanged(bool maximized, bool minimized) => OnStateChanged(maximized, minimized);

    void IHostClient.OnFullscreenChanged(bool fullscreen) => OnFullscreenChanged(fullscreen);

    void IHostClient.OnBackingDestroyed()
    {
        _host = null;
        Set(ref _visible, false, nameof(Visible));
    }

    void IHostClient.OnFailed(Exception error)
    {
        _shell.Log.Error("window", error.ToString());
        var host = _host;
        _host = null;
        host?.Dispose();
        Failed?.Invoke(error);
        if (_visible && !_disposed)
        {
            EnsureHost();
            ApplyPlacement();
            _host!.SetVisible(true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _host?.Dispose();
        _host = null;
        Set(ref _visible, false, nameof(Visible));
    }
}
