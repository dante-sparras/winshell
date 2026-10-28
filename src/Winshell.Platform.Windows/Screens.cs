using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows;

/// <summary>Reads the connected monitors: their names, models, DPI and areas.</summary>
internal static unsafe class Screens
{
    [ThreadStatic]
    private static List<nint>? _found;

    [UnmanagedCallersOnly]
    private static int Collect(nint monitor, nint hdc, RECT* rect, nint data)
    {
        _found!.Add(monitor);
        return 1;
    }

    public static List<(nint Handle, ScreenInfo Info)> Enumerate()
    {
        _found = [];
        Win32.EnumDisplayMonitors(0, 0, &Collect, 0);
        var result = new List<(nint, ScreenInfo)>();
        foreach (var monitor in _found)
        {
            if (Describe(monitor) is { } info)
                result.Add((monitor, info));
        }
        _found = null;
        // Quickshell lists screens in the order the system reports them; put the primary first as Windows does.
        return result.OrderByDescending(r => r.Item2.IsPrimary).ThenBy(r => r.Item2.Geometry.X).ThenBy(r => r.Item2.Geometry.Y).ToList();
    }

    public static ScreenInfo? Describe(nint monitor)
    {
        var mi = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
        if (!Win32.GetMonitorInfo(monitor, ref mi))
            return null;
        var device = new string(mi.szDevice).TrimEnd('\0');
        Win32.GetDpiForMonitor(monitor, 0, out var dpiX, out _);
        var scale = dpiX == 0 ? 1.0 : dpiX / 96.0;
        var (model, id) = MonitorName(device);
        // Logical coordinates: the virtual desktop scaled by each monitor's own factor, as Qt does on Windows.
        var geometry = new QRect(mi.rcMonitor.Left / scale, mi.rcMonitor.Top / scale, mi.rcMonitor.Width / scale, mi.rcMonitor.Height / scale);
        var work = new QRect(mi.rcWork.Left / scale, mi.rcWork.Top / scale, mi.rcWork.Width / scale, mi.rcWork.Height / scale);
        var name = device.Replace(@"\\.\", "", StringComparison.Ordinal);
        return new ScreenInfo(
            device,
            name,
            model,
            Manufacturer(id),
            Serial(id),
            geometry,
            work,
            scale,
            0,
            0,
            RefreshRate(device),
            (mi.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0);
    }

    public static RECT PhysicalBounds(string deviceName, out RECT work)
    {
        foreach (var (handle, info) in Enumerate())
        {
            if (info.Id != deviceName)
                continue;
            var mi = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
            Win32.GetMonitorInfo(handle, ref mi);
            work = mi.rcWork;
            return mi.rcMonitor;
        }
        work = default;
        return default;
    }

    public static nint HandleOf(string deviceName)
    {
        foreach (var (handle, info) in Enumerate())
        {
            if (info.Id == deviceName)
                return handle;
        }
        return 0;
    }

    private static (string Model, string Id) MonitorName(string device)
    {
        var dd = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        if (Win32.EnumDisplayDevicesW(device, 0, ref dd, 1))
        {
            var model = new string(dd.DeviceString).TrimEnd('\0');
            var id = new string(dd.DeviceID).TrimEnd('\0');
            return (model, id);
        }
        return ("", "");
    }

    /// <summary>The three-letter PnP manufacturer code, from a device id like MONITOR\GSM5B7F\...</summary>
    private static string Manufacturer(string id)
    {
        var parts = id.Split('\\');
        return parts.Length > 1 && parts[1].Length >= 3 ? parts[1][..3] : "";
    }

    private static string Serial(string id)
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODEW devMode);

    private static int RefreshRate(string device)
    {
        var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        return EnumDisplaySettingsW(device, -1, ref mode) ? (int)mode.dmDisplayFrequency : 60;
    }
}
