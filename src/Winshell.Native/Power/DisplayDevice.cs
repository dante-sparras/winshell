namespace Winshell.Native;

/// <summary>The monitors Windows lists for a display.</summary>
internal static class DisplayDevice
{
    /// <summary>
    /// The device ID of the monitor on a display such as <c>\\.\DISPLAY1</c>, or "" when Windows has none.
    /// </summary>
    internal static unsafe string MonitorId(string deviceName)
    {
        var info = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        if (!Win32.EnumDisplayDevicesW(deviceName, 0, ref info, 1))
            return "";
        return new string(info.DeviceID).TrimEnd('\0');
    }
}
