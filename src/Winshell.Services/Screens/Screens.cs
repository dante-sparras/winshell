using Winshell.Native;

namespace Winshell;

/// <summary>A connected monitor: its identity, and its physical rectangles and scale.</summary>
internal sealed record ConnectedMonitor(nint Handle, ScreenInfo Info, RECT Bounds, RECT Work, double Scale)
{
    /// <summary>The area a panel or popup on this monitor lays out in: the work area, or the whole monitor without one.</summary>
    public RECT Area => Work.Width > 0 ? Work : Bounds;
}

/// <summary>Reads the connected monitors: their names, models, DPI and areas.</summary>
internal static class Screens
{
    public static List<ConnectedMonitor> Enumerate()
    {
        var result = new List<ConnectedMonitor>();
        foreach (var handle in Monitors.List())
        {
            if (Describe(handle) is { } monitor)
                result.Add(monitor);
        }
        // Put the primary screen first, as Windows does.
        return result.OrderByDescending(m => m.Info.IsPrimary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    /// <summary>The connected monitor with a device name, or null once it is gone.</summary>
    public static ConnectedMonitor? Find(string deviceName) => Enumerate().FirstOrDefault(m => m.Info.Id == deviceName);

    public static ConnectedMonitor? Describe(nint handle)
    {
        if (!Monitors.TryRead(handle, out var mi))
            return null;
        var device = mi.Device;
        Win32.GetDpiForMonitor(handle, 0, out var dpi, out _);
        var scale = DesktopSpace.Scale(dpi);
        var (model, id) = DisplayDevice.Monitor(device);
        var size = MonitorSize.Read(id);
        var name = device.Replace(@"\\.\", "", StringComparison.Ordinal);
        var info = new ScreenInfo(
            device,
            name,
            model,
            Manufacturer(id),
            Serial(id),
            DesktopSpace.ToLogical(mi.Bounds, mi.Bounds, scale),
            DesktopSpace.ToLogical(mi.Work, mi.Bounds, scale),
            scale,
            size.WidthMm,
            size.HeightMm,
            RefreshRate(device),
            mi.IsPrimary);
        return new ConnectedMonitor(handle, info, mi.Bounds, mi.Work, scale);
    }

    /// <summary>The device name and physical bounds of the monitor a window is on, without reading the rest.</summary>
    public static (string Device, RECT Bounds) Of(nint hwnd)
    {
        if (!Monitors.TryRead(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), out var mi))
            return ("", default);
        return (mi.Device, mi.Bounds);
    }

    /// <summary>The three-letter PnP manufacturer code, from an interface path like \\?\DISPLAY#GSM5B7F#...</summary>
    internal static string Manufacturer(string id)
    {
        var parts = id.Split('#');
        return parts.Length > 1 && parts[1].Length >= 3 ? parts[1][..3] : "";
    }

    internal static string Serial(string id)
    {
        var parts = id.Split('#');
        return parts.Length > 2 ? parts[2] : "";
    }

    private static int RefreshRate(string device) => Monitors.RefreshRate(device) is { } hertz ? (int)hertz : 60;
}
