namespace Winshell.Platform.Windows;

/// <summary>
/// When the UI loop should ask Windows for a 1 ms timer. The default interrupt is 15.6 ms, so a requested
/// 16.7 ms frame otherwise waits until the next tick, about 30 ms.
/// </summary>
internal static class FramePace
{
    /// <summary>A wait at or under this many milliseconds uses the 1 ms timer. Longer waits keep the coarse interrupt.</summary>
    public const int FineWaitMilliseconds = 20;

    /// <summary>
    /// True when a frame is already waiting for the next vblank, or the next timer is short enough that the
    /// coarse interrupt would swallow it. A timeout of zero is work that is already due, so it stays coarse.
    /// </summary>
    public static bool NeedsFineTimer(bool framePending, uint timeoutMilliseconds) =>
        framePending || timeoutMilliseconds is > 0 and <= FineWaitMilliseconds;
}
