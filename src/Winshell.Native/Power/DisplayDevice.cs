using Windows.Win32;

namespace Winshell.Native;

/// <summary>The monitors Windows lists for a display.</summary>
internal static class DisplayDevice
{
    /// <summary>
    /// The model Windows names for the monitor on a display such as <c>\\.\DISPLAY1</c>, and its device ID, such as
    /// <c>\\?\DISPLAY#AUS32A8#5&amp;15335277&amp;0&amp;UID184582#{...}</c>. Both "" when Windows has none.
    /// </summary>
    internal static unsafe (string Model, string Id) Monitor(string deviceName)
    {
        var info = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        // Without the interface name, the device ID has a class GUID in place of the instance.
        if (!Win32.EnumDisplayDevicesW(deviceName, 0, ref info, PInvoke.EDD_GET_DEVICE_INTERFACE_NAME))
            return ("", "");
        return (Monitors.Text(info.DeviceString, 128), Monitors.Text(info.DeviceID, 128));
    }
}
