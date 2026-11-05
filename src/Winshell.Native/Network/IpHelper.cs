using Windows.Win32;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.NetworkManagement.Ndis;

namespace Winshell.Native;

/// <summary>The part of iphlpapi that System.Net does not cover: whether an interface is a hardware adapter, and its link.</summary>
internal static class IpHelper
{
    internal readonly record struct InterfaceRow(bool HardwareInterface, bool MediaConnected, ulong LinkSpeed);

    /// <summary>The row of an interface by its GUID, or null when Windows does not know it.</summary>
    internal static InterfaceRow? GetInterface(Guid interfaceGuid)
    {
        var row = default(MIB_IF_ROW2);
        if (PInvoke.ConvertInterfaceGuidToLuid(interfaceGuid, out row.InterfaceLuid) != 0 || PInvoke.GetIfEntry2(ref row) != 0)
            return null;
        var flags = row.InterfaceAndOperStatusFlags;
        return new InterfaceRow(flags.HardwareInterface && !flags.FilterInterface,
            row.MediaConnectState == NET_IF_MEDIA_CONNECT_STATE.MediaConnectStateConnected,
            Math.Max(row.ReceiveLinkSpeed, row.TransmitLinkSpeed));
    }
}
