using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Winshell.Capture;

namespace Winshell;

/// <summary>
/// A live view of a <see cref="CaptureSession"/>. The frame is fitted and centered, keeping its aspect ratio.
/// Its natural size is the frame's physical size divided by this window's scale. The frame is written once into
/// the bitmap, on the thread that delivered it, so the session's loop is this window's. The view lets go of the
/// session when it leaves the tree.
/// </summary>
public sealed class CaptureView : Panel
{
    /// <summary>The capture to show. Null shows nothing.</summary>
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session),
        typeof(CaptureSession),
        typeof(CaptureView),
        new PropertyMetadata(null, (sender, args) => ((CaptureView)sender).OnSessionChanged((CaptureSession?)args.OldValue, (CaptureSession?)args.NewValue)));

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private CaptureSession? _subscribed;
    private WriteableBitmap? _bitmap;
    private ShellWindow? _shell;
    private bool _watchingShell;

    /// <summary>A view with no session, which shows nothing.</summary>
    public CaptureView()
    {
        Children.Add(_image);
        Loaded += (_, _) => OnLoaded();
        Unloaded += (_, _) => OnUnloaded();
    }

    /// <summary>The capture to show. Null shows nothing.</summary>
    public CaptureSession? Session
    {
        get => (CaptureSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// Asks for the frame's natural size, scaled down to fit <paramref name="availableSize"/> with its aspect ratio
    /// kept. Zero without a frame.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = Fit(Natural(), availableSize);
        _image.Measure(size);
        return size;
    }

    /// <summary>Gives the frame all of <paramref name="finalSize"/>; the frame is fitted and centered in it.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _image.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    private static Size Fit(Size natural, Size available)
    {
        if (natural.Width <= 0 || natural.Height <= 0)
            return new Size(0, 0);
        var width = double.IsInfinity(available.Width) ? natural.Width : Math.Min(natural.Width, available.Width);
        var height = double.IsInfinity(available.Height) ? natural.Height : Math.Min(natural.Height, available.Height);
        var fit = Math.Min(width / natural.Width, height / natural.Height);
        return new Size(natural.Width * fit, natural.Height * fit);
    }

    private void OnLoaded()
    {
        AttachShell();
        Subscribe(Session);
    }

    private void OnUnloaded()
    {
        Subscribe(null);
        DetachShell();
    }

    private void OnSessionChanged(CaptureSession? previous, CaptureSession? next)
    {
        if (IsLoaded)
            Subscribe(next);
    }

    private void Subscribe(CaptureSession? session)
    {
        if (ReferenceEquals(_subscribed, session))
        {
            Show(session?.Frame);
            return;
        }
        if (_subscribed is not null)
            _subscribed.PropertyChanged -= OnFrameChanged;
        _subscribed = session;
        if (session is not null)
            session.PropertyChanged += OnFrameChanged;
        Show(session?.Frame);
    }

    private void OnFrameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _subscribed) && e.PropertyName is nameof(CaptureSession.Frame) or null or "")
            Show(_subscribed?.Frame);
    }

    private void Show(PixelImage? frame)
    {
        if (frame is null)
        {
            if (_bitmap is null)
                return;
            _bitmap = null;
            _image.Source = null;
            InvalidateMeasure();
            return;
        }
        WriteableBitmap bitmap;
        var resized = false;
        if (_bitmap is { } current && current.PixelWidth == frame.Width && current.PixelHeight == frame.Height)
            bitmap = current;
        else
        {
            bitmap = new WriteableBitmap(frame.Width, frame.Height);
            resized = true;
        }
        Copy(bitmap, frame);
        bitmap.Invalidate();
        _bitmap = bitmap;
        if (!ReferenceEquals(_image.Source, bitmap))
            _image.Source = bitmap;
        if (resized)
            InvalidateMeasure();
    }

    /// <summary>One write into the bitmap. Capture frames are already premultiplied, which is what the bitmap stores.</summary>
    private static void Copy(WriteableBitmap bitmap, PixelImage frame)
    {
        using var destination = bitmap.PixelBuffer.AsStream();
        var pixels = frame.Bgra;
        if (frame.Premultiplied)
        {
            destination.Write(pixels);
            return;
        }
        // Premultiplied a piece at a time, so a frame needs no second array.
        Span<byte> piece = stackalloc byte[4096];
        for (var start = 0; start < pixels.Length; start += piece.Length)
        {
            var source = pixels.Slice(start, Math.Min(piece.Length, pixels.Length - start));
            for (var i = 0; i < source.Length; i += 4)
            {
                var alpha = source[i + 3];
                piece[i + 3] = alpha;
                if (alpha == 255)
                {
                    piece[i] = source[i];
                    piece[i + 1] = source[i + 1];
                    piece[i + 2] = source[i + 2];
                }
                else
                {
                    piece[i] = (byte)((source[i] * alpha + 127) / 255);
                    piece[i + 1] = (byte)((source[i + 1] * alpha + 127) / 255);
                    piece[i + 2] = (byte)((source[i + 2] * alpha + 127) / 255);
                }
            }
            destination.Write(piece[..source.Length]);
        }
    }

    private Size Natural()
    {
        if (_bitmap is null)
            return new Size(0, 0);
        var scale = ScaleOf();
        return new Size(_bitmap.PixelWidth / scale, _bitmap.PixelHeight / scale);
    }

    private double ScaleOf()
    {
        var scale = _shell?.Scale ?? 0;
        if (scale <= 0)
            scale = XamlRoot?.RasterizationScale ?? 1;
        return scale > 0 ? scale : 1;
    }

    private void AttachShell()
    {
        DetachShell();
        _shell = ShellWindow.Of(this);
        if (_shell is null)
            return;
        _shell.PropertyChanged += OnShellChanged;
        _watchingShell = true;
    }

    private void DetachShell()
    {
        if (!_watchingShell || _shell is null)
            return;
        _shell.PropertyChanged -= OnShellChanged;
        _watchingShell = false;
        _shell = null;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellWindow.Scale))
            InvalidateMeasure();
    }
}
