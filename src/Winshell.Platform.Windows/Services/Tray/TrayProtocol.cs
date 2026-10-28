namespace Winshell.Platform.Windows.Services.Tray;

/// <summary>How Explorer encodes a click it sends back to a notification-area icon.</summary>
internal static class TrayProtocol
{
    public const uint Version = 3;

    public const uint Version4 = 4;

    /// <summary>
    /// The <c>wParam</c> and <c>lParam</c> of the icon's callback. Version 4 carries the screen point and the icon
    /// id; older versions carry the icon id and the mouse message.
    /// </summary>
    public static (nint WParam, nint LParam) Callback(uint version, uint iconId, uint message, int x, int y)
    {
        if (version >= Version4)
        {
            var w = (uint)(x & 0xffff) | ((uint)(y & 0xffff) << 16);
            var l = (message & 0xffff) | ((iconId & 0xffff) << 16);
            return ((nint)w, (nint)l);
        }
        return ((nint)iconId, (nint)message);
    }
}
