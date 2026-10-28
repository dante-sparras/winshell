using SkiaSharp;

namespace Winshell.Platform.Windows.Services.Capture;

/// <summary>A source in physical pixels. Monitor ids are Windows display device names.</summary>
internal sealed record CaptureTarget(nint Window, string MonitorId);

/// <summary>Owned by one session, used only on its engine thread. Windows closure is posted to that thread.</summary>
internal interface ICaptureBackend : IDisposable
{
    void Start(CaptureTarget target, bool includeCursor, Action closed);

    /// <summary>Transfers ownership of the latest image, skipping superseded frames. Null means no new frame.</summary>
    SKImage? ReadLatest();
}
