using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace Winshell.Native;

/// <summary>The DPI awareness of the calling thread.</summary>
internal static class ThreadDpi
{
    internal static bool IsPerMonitorV2()
    {
        return PInvoke.AreDpiAwarenessContextsEqual(
            PInvoke.GetThreadDpiAwarenessContext(),
            new DPI_AWARENESS_CONTEXT(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));
    }
}
