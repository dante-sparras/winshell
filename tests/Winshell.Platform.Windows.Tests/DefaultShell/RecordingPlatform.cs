using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>A platform with two screens that records the windows a shell makes, so a test can load the whole default shell.</summary>
internal sealed class RecordingPlatform : IShellPlatform
{
    public List<ScreenInfo> ScreenList { get; } =
    [
        new("1", "DISPLAY1", "Monitor A", "Acme", "S1", new QRect(0, 0, 1920, 1080), new QRect(0, 0, 1920, 1040), 1, 600, 340, 60, true),
        new("2", "DISPLAY2", "Monitor B", "Acme", "S2", new QRect(1920, 0, 2560, 1440), new QRect(1920, 0, 2560, 1440), 1.5, 600, 340, 144, false),
    ];

    public IReadOnlyList<ScreenInfo> Screens => ScreenList;

    public event Action? ScreensChanged;

    public void ChangeScreens() => ScreensChanged?.Invoke();

    public List<RecordingWindow> Windows { get; } = [];

    public IWindowBackend CreateWindow(WindowKind kind, IWindowClient client)
    {
        var window = new RecordingWindow(client, this);
        Windows.Add(window);
        return window;
    }
}

/// <summary>A window backend that keeps what the shell asked of it.</summary>
internal sealed class RecordingWindow(IWindowClient client, RecordingPlatform platform) : IWindowBackend, IRebindable
{
    public IWindowClient Client { get; private set; } = client;

    public PanelPlacement? Panel { get; private set; }

    public bool IsVisible { get; private set; }

    public QPoint Origin { get; private set; }

    public WindowBackdrop Backdrop { get; private set; }

    public WindowCorners Corners { get; private set; }

    public void Rebind(IWindowClient next) => Client = next;

    public void SetPanel(PanelPlacement placement)
    {
        Panel = placement;
        var scale = placement.Screen?.DevicePixelRatio ?? 1;
        Client.OnResized(Math.Max(1, placement.Width), Math.Max(1, placement.Height), scale);
    }

    public void SetPopup(PopupPlacement placement) => Client.OnResized(placement.Rect.Width, placement.Rect.Height, 1);

    public void SetFloating(FloatingPlacement placement) => Client.OnResized(placement.Width, placement.Height, 1);

    public void SetVisible(bool visible) => IsVisible = visible;

    public void SetInputRegion(IReadOnlyList<QRect>? rects)
    {
    }

    public void SetCursor(int shape)
    {
    }

    public void SetBackdrop(WindowBackdrop backdrop, WindowCorners corners, bool darkFrame)
    {
        Backdrop = backdrop;
        Corners = corners;
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

    /// <summary>How many times the shell brought the window to the front with keyboard focus.</summary>
    public int Activations { get; private set; }

    public void SetKeyboardFocus(bool focus)
    {
        if (focus)
            Activations++;
    }

    public (int X, int Y) MapToScreen(double x, double y) => ((int)Math.Round(Origin.X + x), (int)Math.Round(Origin.Y + y));

    public SKBitmap Render()
    {
        var scene = Client.Scene.Root;
        var bitmap = new SKBitmap(Math.Max(1, (int)Math.Ceiling(scene.Width)), Math.Max(1, (int)Math.Ceiling(scene.Height)));
        using var canvas = new SKCanvas(bitmap);
        Client.OnPaint(canvas);
        return bitmap;
    }

    public void Dispose() => platform.Windows.Remove(this);
}
