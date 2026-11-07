using Windows.Foundation;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// A connected monitor's handle, device name such as <c>\\.\DISPLAY1</c>, physical rectangles and scale: what placing
/// a window needs. Reading it asks Windows nothing about the monitor's model, size or refresh rate.
/// </summary>
internal sealed record MonitorGeometry(nint Handle, string Id, RECT Bounds, RECT Work, double Scale, bool IsPrimary)
{
    /// <summary>The area a window on this monitor lays out in: the work area, or the whole monitor when that area has no size.</summary>
    public RECT Area => Work.Width > 0 && Work.Height > 0 ? Work : Bounds;

    /// <summary>The whole monitor in logical pixels.</summary>
    public Rect Logical => DesktopSpace.ToLogical(Bounds, Bounds, Scale);

    /// <summary>The area a window is placed in, with the scale that turns its pixels into logical ones.</summary>
    public MonitorFrame Frame => new(Area.Left, Area.Top, Area.Right, Area.Bottom, Scale);
}

/// <summary>
/// A described monitor: its identity, its device interface path such as
/// <c>\\?\DISPLAY#AUS32A8#5&amp;15335277&amp;0&amp;UID184582#{...}</c> ("" when Windows has none), and its physical
/// rectangles and scale.
/// </summary>
internal sealed record ConnectedMonitor(
    nint Handle,
    ScreenInfo Info,
    string InterfacePath,
    RECT Bounds,
    RECT Work,
    double Scale)
{
    /// <summary>The monitor's PnP model code, such as <c>AUS32A8</c>, or "" when Windows names none.</summary>
    public string ModelCode => Screens.ModelCode(InterfacePath);
}

/// <summary>
/// Reads the connected monitors. <see cref="List"/>, <see cref="Find"/>, <see cref="Primary"/> and <see cref="Read"/>
/// read only their geometry. <see cref="Describe"/> adds the names, model, size and refresh rate of one, and
/// <see cref="Enumerate"/> describes them all.
/// </summary>
internal static class Screens
{
    /// <summary>Every connected monitor's geometry: the primary one first, then left to right and top to bottom.</summary>
    public static List<MonitorGeometry> List()
    {
        var result = new List<MonitorGeometry>();
        foreach (var handle in Monitors.List())
            if (Read(handle) is { } monitor)
                result.Add(monitor);
        return result.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top)
            .ToList();
    }

    /// <summary>Every connected monitor, described, the primary one first.</summary>
    public static List<ConnectedMonitor> Enumerate()
    {
        return List().Select(Describe).ToList();
    }

    /// <summary>The connected monitor with a device name, or null once it is gone.</summary>
    public static MonitorGeometry? Find(string deviceName)
    {
        return List().FirstOrDefault(m => m.Id == deviceName);
    }

    /// <summary>The primary monitor, or the first one when Windows marks none, or null when none is connected.</summary>
    public static MonitorGeometry? Primary()
    {
        return List().FirstOrDefault();
    }

    /// <summary>The geometry of the monitor with this handle, or null once it is gone.</summary>
    public static MonitorGeometry? Read(nint handle)
    {
        if (!Monitors.TryRead(handle, out var mi))
            return null;
        Win32.GetDpiForMonitor(handle, 0, out var dpi, out _);
        return new MonitorGeometry(handle, mi.Device, mi.Bounds, mi.Work, DesktopSpace.Scale(dpi), mi.IsPrimary);
    }

    /// <summary>A monitor's geometry with its names, model, size and refresh rate.</summary>
    public static ConnectedMonitor Describe(MonitorGeometry monitor)
    {
        var device = monitor.Id;
        var (model, id) = DisplayDevice.Monitor(device);
        var size = MonitorSize.Read(id);
        var name = device.Replace(@"\\.\", "", StringComparison.Ordinal);
        var info = new ScreenInfo(
            device,
            name,
            model,
            Manufacturer(ModelCode(id)),
            Serial(id),
            monitor.Logical,
            DesktopSpace.ToLogical(monitor.Work, monitor.Bounds, monitor.Scale),
            monitor.Scale,
            size.WidthMm,
            size.HeightMm,
            RefreshRate(device),
            monitor.IsPrimary);
        return new ConnectedMonitor(monitor.Handle, info, id, monitor.Bounds, monitor.Work, monitor.Scale);
    }

    /// <summary>The device name and physical bounds of the monitor a window is on, without reading the rest.</summary>
    public static (string Device, RECT Bounds) Of(nint hwnd)
    {
        if (!Monitors.TryRead(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), out var mi))
            return ("", default);
        return (mi.Device, mi.Bounds);
    }

    /// <summary>The PnP model code, such as <c>GSM5B7F</c>, in an interface path like <c>\\?\DISPLAY#GSM5B7F#...</c>.</summary>
    internal static string ModelCode(string id)
    {
        var parts = id.Split('#');
        return parts.Length > 1 ? parts[1] : "";
    }

    /// <summary>The three-letter PnP manufacturer code that starts a model code.</summary>
    internal static string Manufacturer(string modelCode)
    {
        return modelCode.Length >= 3 ? modelCode[..3] : "";
    }

    internal static string Serial(string id)
    {
        var parts = id.Split('#');
        return parts.Length > 2 ? parts[2] : "";
    }

    private static int RefreshRate(string device)
    {
        return Monitors.RefreshRate(device) is { } hertz ? (int)hertz : 60;
    }
}
