using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows;

/// <summary>A connected monitor: what QML sees of it, and its physical rectangles and scale.</summary>
internal sealed record Monitor(nint Handle, ScreenInfo Info, RECT Bounds, RECT Work, double Scale)
{
    /// <summary>The area a panel or popup on this monitor lays out in: the work area, or the whole monitor without one.</summary>
    public RECT Area => Work.Width > 0 ? Work : Bounds;
}

/// <summary>Reads the connected monitors: their names, models, DPI and areas.</summary>
internal static unsafe partial class Screens
{
    [ThreadStatic]
    private static List<nint>? _found;

    [UnmanagedCallersOnly]
    private static int Collect(nint monitor, nint hdc, RECT* rect, nint data)
    {
        _found!.Add(monitor);
        return 1;
    }

    public static List<Monitor> Enumerate()
    {
        _found = [];
        Win32.EnumDisplayMonitors(0, 0, &Collect, 0);
        var result = new List<Monitor>();
        foreach (var handle in _found)
        {
            if (Describe(handle) is { } monitor)
                result.Add(monitor);
        }
        _found = null;
        // Put the primary screen first, as Windows does.
        return result.OrderByDescending(m => m.Info.IsPrimary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    /// <summary>The connected monitor with a device name, or null once it is gone.</summary>
    public static Monitor? Find(string deviceName) => Enumerate().FirstOrDefault(m => m.Info.Id == deviceName);

    public static Monitor? Describe(nint handle)
    {
        var mi = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
        if (!Win32.GetMonitorInfo(handle, ref mi))
            return null;
        var device = new string(mi.szDevice).TrimEnd('\0');
        Win32.GetDpiForMonitor(handle, 0, out var dpi, out _);
        var scale = DesktopSpace.Scale(dpi);
        var (model, id) = MonitorName(device);
        var name = device.Replace(@"\\.\", "", StringComparison.Ordinal);
        var info = new ScreenInfo(
            device,
            name,
            model,
            Manufacturer(id),
            Serial(id),
            DesktopSpace.ToLogical(mi.rcMonitor, mi.rcMonitor, scale),
            DesktopSpace.ToLogical(mi.rcWork, mi.rcMonitor, scale),
            scale,
            0,
            0,
            RefreshRate(device),
            (mi.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0);
        return new Monitor(handle, info, mi.rcMonitor, mi.rcWork, scale);
    }

    /// <summary>The device name and physical bounds of the monitor a window is on, without reading the rest.</summary>
    public static (string Device, RECT Bounds) Of(nint hwnd)
    {
        var mi = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
        if (!Win32.GetMonitorInfo(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), ref mi))
            return ("", default);
        return (new string(mi.szDevice).TrimEnd('\0'), mi.rcMonitor);
    }

    private static (string Model, string Id) MonitorName(string device)
    {
        var dd = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        // Flag 0 returns MONITOR\CODE\serial. Flag 1 is the interface path \\?\DISPLAY#..., which does not split.
        if (Win32.EnumDisplayDevicesW(device, 0, ref dd, 0))
        {
            var model = new string(dd.DeviceString).TrimEnd('\0');
            var id = new string(dd.DeviceID).TrimEnd('\0');
            return (model, id);
        }
        return ("", "");
    }

    /// <summary>The three-letter PnP manufacturer code, from a device id like MONITOR\GSM5B7F\...</summary>
    internal static string Manufacturer(string id)
    {
        var parts = id.Split('\\');
        return parts.Length > 1 && parts[1].Length >= 3 ? parts[1][..3] : "";
    }

    internal static string Serial(string id)
    {
        var parts = id.Split('\\');
        return parts.Length > 2 ? parts[2] : "";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        public fixed char dmDeviceName[32];
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        public fixed char dmFormName[32];
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettingsW(string device, int mode, ref DEVMODEW devMode);

    private static int RefreshRate(string device)
    {
        var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        return EnumDisplaySettingsW(device, -1, ref mode) ? (int)mode.dmDisplayFrequency : 60;
    }
}
