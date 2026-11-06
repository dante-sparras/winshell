using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace Winshell.Native;

/// <summary>A monitor's device name, such as <c>\\.\DISPLAY1</c>, and its rectangles in physical pixels.</summary>
internal readonly record struct MonitorInfo(string Device, RECT Bounds, RECT Work, bool IsPrimary);

/// <summary>Reads the connected monitors from Windows.</summary>
internal static unsafe class Monitors
{
    [ThreadStatic] private static List<nint>? _found;

    /// <summary>The handle of every connected monitor, in the order Windows lists them.</summary>
    internal static List<nint> List()
    {
        var found = new List<nint>();
        _found = found;
        try
        {
            Win32.EnumDisplayMonitors(0, 0, &Collect, 0);
        }
        finally
        {
            _found = null;
        }

        return found;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint monitor, nint hdc, RECT* rect, nint data)
    {
        _found?.Add(monitor);
        return 1;
    }

    /// <summary>The monitor's device name and rectangles. False once the monitor is gone.</summary>
    internal static bool TryRead(nint monitor, out MonitorInfo info)
    {
        var mi = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
        if (!Win32.GetMonitorInfo(monitor, ref mi))
        {
            info = default;
            return false;
        }

        info = new MonitorInfo(Text(mi.szDevice, 32), mi.rcMonitor, mi.rcWork,
            (mi.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0);
        return true;
    }

    /// <summary>The display device's current refresh rate in hertz, or null when Windows does not say.</summary>
    internal static uint? RefreshRate(string device)
    {
        var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        return PInvoke.EnumDisplaySettings(device, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode)
            ? mode.dmDisplayFrequency
            : null;
    }

    /// <summary>A fixed character buffer up to its first null, never past its capacity.</summary>
    internal static string Text(char* buffer, int capacity)
    {
        var text = new ReadOnlySpan<char>(buffer, capacity);
        var end = text.IndexOf('\0');
        return new string(end < 0 ? text : text[..end]);
    }
}
