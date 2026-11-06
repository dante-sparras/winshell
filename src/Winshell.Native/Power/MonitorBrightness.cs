using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Winshell.Native;

/// <summary>
/// An external monitor's brightness over DDC/CI, through the first physical monitor behind a display monitor. Each
/// call gets the physical monitors and destroys them before it returns.
/// </summary>
internal static class MonitorBrightness
{
    /// <summary>A display monitor with more physical monitors than this counts as a failed read.</summary>
    private const uint MaxPhysicalMonitors = 16;

    /// <summary>The brightness range and level of a display monitor, or false when it does not answer.</summary>
    internal static bool TryRead(nint monitor, out int minimum, out int current, out int maximum)
    {
        uint low = 0, now = 0, high = 0;
        var ok = WithFirstPhysical(monitor,
            physical => PInvoke.GetMonitorBrightness(physical, out low, out now, out high) != 0);
        minimum = (int)low;
        current = (int)now;
        maximum = (int)high;
        return ok && maximum >= minimum;
    }

    /// <summary>Sets a display monitor's brightness. False when it does not answer.</summary>
    internal static bool TrySet(nint monitor, uint brightness)
    {
        return WithFirstPhysical(monitor, physical => PInvoke.SetMonitorBrightness(physical, brightness) != 0);
    }

    private static bool WithFirstPhysical(nint monitor, Func<HANDLE, bool> use)
    {
        var display = new HMONITOR(monitor);
        if (!PInvoke.GetNumberOfPhysicalMonitorsFromHMONITOR(display, out var count) || count == 0 ||
            count > MaxPhysicalMonitors)
            return false;
        Span<PHYSICAL_MONITOR> monitors = stackalloc PHYSICAL_MONITOR[(int)count];
        if (!PInvoke.GetPhysicalMonitorsFromHMONITOR(display, monitors))
            return false;
        try
        {
            return use(monitors[0].hPhysicalMonitor);
        }
        finally
        {
            PInvoke.DestroyPhysicalMonitors(monitors);
        }
    }
}
