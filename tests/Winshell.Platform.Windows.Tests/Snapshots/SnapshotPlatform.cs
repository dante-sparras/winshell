using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.Snapshots;

/// <summary>
/// One 1920×1080 screen at a given scale, with a 48 pixel taskbar's worth of work area taken. Its windows are sized
/// as Windows sizes them, and are painted offscreen the way the composition surface paints them.
/// </summary>
internal sealed class SnapshotPlatform(double scale) : IShellPlatform
{
    public ScreenInfo Screen { get; } = new("1", "DISPLAY1", "Monitor A", "Acme", "S1",
        new QRect(0, 0, 1920, 1080), new QRect(0, 0, 1920, 1032), scale, 600, 340, 60, true);

    public IReadOnlyList<ScreenInfo> Screens => [Screen];

    public event Action? ScreensChanged
    {
        add { }
        remove { }
    }

    public List<SnapshotWindow> Windows { get; } = [];

    public IWindowBackend CreateWindow(WindowKind kind, IWindowClient client)
    {
        var window = new SnapshotWindow(client, this);
        Windows.Add(window);
        return window;
    }
}

internal sealed class SnapshotWindow(IWindowClient client, SnapshotPlatform platform) : IWindowBackend, IRebindable
{
    public IWindowClient Client { get; private set; } = client;

    public bool IsVisible { get; private set; }

    public QPoint Origin { get; private set; }

    public double Width { get; private set; }

    public double Height { get; private set; }

    public void Rebind(IWindowClient next) => Client = next;

    public void SetPanel(PanelPlacement placement)
    {
        var screen = platform.Screen.Geometry;
        var w = placement.Anchors.HasFlag(Edges.Left) && placement.Anchors.HasFlag(Edges.Right) ? screen.Width - placement.MarginLeft - placement.MarginRight : placement.Width;
        var h = placement.Anchors.HasFlag(Edges.Top) && placement.Anchors.HasFlag(Edges.Bottom) ? screen.Height - placement.MarginTop - placement.MarginBottom : placement.Height;
        var x = placement.Anchors.HasFlag(Edges.Left) ? placement.MarginLeft
            : placement.Anchors.HasFlag(Edges.Right) ? screen.Width - w - placement.MarginRight
            : (screen.Width - w) / 2;
        var y = placement.Anchors.HasFlag(Edges.Top) ? placement.MarginTop
            : placement.Anchors.HasFlag(Edges.Bottom) ? screen.Height - h - placement.MarginBottom
            : (screen.Height - h) / 2;
        Place(x, y, w, h);
    }

    public void SetPopup(PopupPlacement placement) => Place(placement.Rect.X, placement.Rect.Y, placement.Rect.Width, placement.Rect.Height);

    public void SetFloating(FloatingPlacement placement) => Place(0, 0, placement.Width, placement.Height);

    private void Place(double x, double y, double width, double height)
    {
        Origin = new QPoint(x, y);
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Client.OnResized(Width, Height, platform.Screen.DevicePixelRatio);
    }

    public void SetVisible(bool visible) => IsVisible = visible;

    public void SetInputRegion(IReadOnlyList<QRect>? rects)
    {
    }

    public void SetOpacity(double opacity)
    {
    }

    public void SetVisibleMask(QRect? bounds)
    {
    }

    public void SetCursor(int shape)
    {
    }

    public void SetBackdrop(WindowBackdrop backdrop, WindowCorners corners, bool darkFrame)
    {
    }

    public void RequestFrame()
    {
    }

    public void Raise()
    {
    }

    public void StartSystemMove()
    {
    }

    public void StartSystemResize(Edges edges)
    {
    }

    public void SetKeyboardFocus(bool focus)
    {
    }

    public (int X, int Y) MapToScreen(double x, double y)
    {
        var scale = platform.Screen.DevicePixelRatio;
        return ((int)Math.Round((Origin.X + x) * scale), (int)Math.Round((Origin.Y + y) * scale));
    }

    /// <summary>
    /// Paints the window into a premultiplied BGRA surface in physical pixels, as the composition surface does, then
    /// puts it over <paramref name="backdrop"/>: the color that stands in for the Mica or Acrylic that Windows draws
    /// behind it.
    /// </summary>
    public SKBitmap Render(SKColor backdrop)
    {
        var scale = platform.Screen.DevicePixelRatio;
        var info = new SKImageInfo((int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var window = new SKBitmap(info);
        using (var pixmap = window.PeekPixels())
        using (var surface = SKSurface.Create(pixmap))
            Client.OnPaint(surface.Canvas);
        var result = new SKBitmap(info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.Clear(backdrop);
        canvas.DrawBitmap(window, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), null);
        return result;
    }

    public void Dispose() => platform.Windows.Remove(this);
}
