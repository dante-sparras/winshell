using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Winshell.Native;
using Winshell.Windows;
using static Winshell.Windows.NativeMethods;

namespace Winshell;

/// <summary>
/// A live preview of an app window, drawn by DWM into the shell window that holds this element. The image is in
/// physical pixels relative to that window, and it is painted over the XAML, so nothing can overlap it. It follows
/// this element's layout and the shell window moving, and it lets go of the preview when it leaves the tree.
/// </summary>
public sealed class WindowThumbnail : FrameworkElement
{
    /// <summary>The app window to preview, from <see cref="WindowListService.Windows"/>. Null shows nothing.</summary>
    public static readonly DependencyProperty WindowProperty = DependencyProperty.Register(
        nameof(Window),
        typeof(AppWindow),
        typeof(WindowThumbnail),
        new PropertyMetadata(null, (sender, args) => ((WindowThumbnail)sender).OnWindowChanged((AppWindow?)args.OldValue, (AppWindow?)args.NewValue)));

    /// <summary>
    /// While true, Windows shows <see cref="Window"/> at full size and hides the others. It stays off when the user
    /// turned peek off, and it ends when this element unloads or the window goes.
    /// </summary>
    public static readonly DependencyProperty PeekProperty = DependencyProperty.Register(
        nameof(Peek),
        typeof(bool),
        typeof(WindowThumbnail),
        new PropertyMetadata(false, (sender, _) => ((WindowThumbnail)sender).UpdatePeek()));

    private ShellWindow? _shell;
    private WindowPeek? _peeker;
    private nint _thumbnail;
    private nint _destination;
    private RECT _insets;
    private ThumbnailPaint _applied;
    private bool _watching;

    /// <summary>
    /// A thumbnail with no window, which shows nothing. It takes no clicks, and screen readers skip it.
    /// </summary>
    public WindowThumbnail()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Loaded += (_, _) => OnLoaded();
        Unloaded += (_, _) => OnUnloaded();
        SizeChanged += (_, _) => UpdateThumbnail();
    }

    /// <summary>The app window to preview, from <see cref="WindowListService.Windows"/>. Null shows nothing.</summary>
    public AppWindow? Window
    {
        get => (AppWindow?)GetValue(WindowProperty);
        set => SetValue(WindowProperty, value);
    }

    /// <summary>
    /// While true, Windows shows <see cref="Window"/> at full size and hides the others. It stays off when the user
    /// turned peek off, and it ends when this element unloads or the window goes.
    /// </summary>
    public bool Peek
    {
        get => (bool)GetValue(PeekProperty);
        set => SetValue(PeekProperty, value);
    }

    /// <summary>A move inside the shell window does not change the size, and the preview still has to follow.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        UpdateThumbnail();
        return size;
    }

    private void OnLoaded()
    {
        WatchClosed(Window, true);
        AttachShell();
        UpdateThumbnail();
        UpdatePeek();
    }

    private void OnUnloaded()
    {
        WatchClosed(Window, false);
        DetachShell();
        ReleaseThumbnail();
        _peeker?.End();
    }

    private void OnWindowChanged(AppWindow? previous, AppWindow? next)
    {
        // Only while loaded. A thumbnail that has left the tree must not keep the window alive.
        if (IsLoaded)
        {
            WatchClosed(previous, false);
            WatchClosed(next, true);
        }
        _insets = default;
        ReleaseThumbnail();
        UpdateThumbnail();
        UpdatePeek();
    }

    private void WatchClosed(AppWindow? window, bool watch)
    {
        if (window is null)
            return;
        if (watch)
            window.Closed += OnClosed;
        else
            window.Closed -= OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e) => Window = null;

    private void AttachShell()
    {
        DetachShell();
        _shell = ShellWindow.Of(this);
        if (_shell is null)
            return;
        _shell.PropertyChanged += OnShellChanged;
        _watching = true;
    }

    private void DetachShell()
    {
        if (!_watching || _shell is null)
            return;
        _shell.PropertyChanged -= OnShellChanged;
        _watching = false;
        _shell = null;
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The destination rect is relative to the shell window, and a move or a scale change puts this element
        // somewhere else in that window.
        if (e.PropertyName is nameof(ShellWindow.Origin) or nameof(ShellWindow.Scale) or nameof(ShellWindow.ActualWidth) or nameof(ShellWindow.ActualHeight))
            UpdateThumbnail();
    }

    private void UpdatePeek()
    {
        var hwnd = Peek && Window is { } window ? window.Hwnd : 0;
        if (hwnd == 0 && _peeker is null)
            return;
        _peeker ??= WindowPeek.ForSystem();
        _peeker.Show(hwnd, _shell?.Host?.Handle ?? 0);
    }

    private void UpdateThumbnail()
    {
        if (!IsLoaded || Window is not { Hwnd: > 0 } window)
        {
            ReleaseThumbnail();
            return;
        }
        var destination = _shell?.Host?.Handle ?? 0;
        if (destination == 0 || !TryBox(out var box))
        {
            HideThumbnail();
            return;
        }
        if (_thumbnail != 0 && _destination != destination)
            ReleaseThumbnail();
        if (_thumbnail == 0)
        {
            if (DwmRegisterThumbnail(destination, window.Hwnd, out var thumbnail) != 0)
                return;
            _thumbnail = thumbnail;
            _destination = destination;
            _applied = default;
        }
        if (DwmQueryThumbnailSourceSize(_thumbnail, out var size) != 0 || size.Width <= 0 || size.Height <= 0)
            return;
        var source = SourceRect(window.Hwnd, size.Width, size.Height);
        var paint = new ThumbnailPaint(
            WindowRules.FitPreview(box, source.Width, source.Height),
            source,
            OpacityOf(),
            Visible: true);
        if (paint == _applied)
            return;
        if (Apply(paint))
            _applied = paint;
    }

    /// <summary>The window without its invisible resize borders. A minimized window keeps the borders it had while shown.</summary>
    private RECT SourceRect(nint hwnd, int width, int height)
    {
        if (!Win32.IsIconic(hwnd) && Win32.GetWindowRect(hwnd, out var window)
            && Win32.DwmGetWindowRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, Marshal.SizeOf<RECT>()) == 0)
        {
            var visible = WindowRules.PreviewSource(window, frame);
            _insets = new RECT(visible.Left, visible.Top, window.Width - visible.Right, window.Height - visible.Bottom);
        }
        var inset = new RECT(_insets.Left, _insets.Top, width - _insets.Right, height - _insets.Bottom);
        return inset.Width > 0 && inset.Height > 0 ? inset : new RECT(0, 0, width, height);
    }

    private bool TryBox(out RECT box)
    {
        box = default;
        if (Visibility != Visibility.Visible || ActualWidth <= 0 || ActualHeight <= 0 || XamlRoot?.Content is not UIElement root)
            return false;
        GeneralTransform transform;
        try
        {
            transform = TransformToVisual(root);
        }
        catch (ArgumentException)
        {
            return false;
        }
        var scale = XamlRoot.RasterizationScale;
        if (scale <= 0)
            scale = 1;
        var top = transform.TransformPoint(default);
        var bottom = transform.TransformPoint(new Point(ActualWidth, ActualHeight));
        box = new RECT(
            (int)Math.Round(top.X * scale),
            (int)Math.Round(top.Y * scale),
            (int)Math.Round(bottom.X * scale),
            (int)Math.Round(bottom.Y * scale));
        return box.Width > 0 && box.Height > 0;
    }

    private byte OpacityOf()
    {
        var opacity = 1.0;
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not UIElement element)
                continue;
            if (element.Visibility != Visibility.Visible)
                return 0;
            opacity *= element.Opacity;
        }
        return (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
    }

    private void HideThumbnail()
    {
        if (_thumbnail == 0 || !_applied.Visible)
            return;
        var hidden = _applied with { Visible = false };
        if (Apply(hidden))
            _applied = hidden;
    }

    private bool Apply(ThumbnailPaint paint)
    {
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            Flags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY | DWM_TNP_VISIBLE,
            Destination = paint.Destination,
            Source = paint.Source,
            Opacity = paint.Opacity,
            Visible = paint.Visible ? 1 : 0,
        };
        return Win32.DwmUpdateThumbnailProperties(_thumbnail, properties) == 0;
    }

    private void ReleaseThumbnail()
    {
        if (_thumbnail != 0)
            _ = DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = 0;
        _destination = 0;
        _applied = default;
    }

    private readonly record struct ThumbnailPaint(RECT Destination, RECT Source, byte Opacity, bool Visible);
}
