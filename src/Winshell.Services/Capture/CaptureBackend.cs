using Winshell.Windows;

namespace Winshell.Capture;

/// <summary>A source in physical pixels. Monitor ids are Windows display device names.</summary>
public sealed record CaptureTarget(nint Window, string MonitorId)
{
    /// <summary>The app window this was made from, or null for a handle or a screen.</summary>
    internal AppWindow? Source { get; init; }

    /// <summary>A top-level window, by its handle.</summary>
    public static CaptureTarget ForWindow(nint window) => new(window, "");

    /// <summary>An <see cref="AppWindow"/> from <see cref="WindowListService"/>. The caller does not need its handle.</summary>
    public static CaptureTarget ForWindow(AppWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new(window.Hwnd, "") { Source = window };
    }

    public static implicit operator CaptureTarget(AppWindow window) => ForWindow(window);

    public static CaptureTarget ForScreen(ScreenInfo screen) => new(0, screen.Id);
}

/// <summary>Owned by one session, used only on its loop's thread. Windows closure is posted to that thread.</summary>
internal interface ICaptureBackend : IDisposable
{
    void Start(CaptureTarget target, bool includeCursor, Action closed);

    /// <summary>The latest image, skipping superseded frames. Null means no new frame.</summary>
    PixelImage? ReadLatest();
}
