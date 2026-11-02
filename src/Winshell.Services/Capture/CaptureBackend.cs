namespace Winshell.Capture;

/// <summary>A source in physical pixels. Monitor ids are Windows display device names.</summary>
public sealed record CaptureTarget(nint Window, string MonitorId)
{
    /// <summary>A top-level window, by its handle.</summary>
    public static CaptureTarget ForWindow(nint window) => new(window, "");

    public static CaptureTarget ForScreen(ScreenInfo screen) => new(0, screen.Id);
}

/// <summary>Owned by one session, used only on its loop's thread. Windows closure is posted to that thread.</summary>
internal interface ICaptureBackend : IDisposable
{
    void Start(CaptureTarget target, bool includeCursor, Action closed);

    /// <summary>The latest image, skipping superseded frames. Null means no new frame.</summary>
    PixelImage? ReadLatest();
}
