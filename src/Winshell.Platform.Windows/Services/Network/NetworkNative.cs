using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Network;

/// <summary>The parts of iphlpapi and wlanapi that WinRT and System.Net do not cover.</summary>
internal static unsafe partial class NetworkNative
{
    // iphlpapi: whether an interface is a hardware adapter, and its link.

    /// <summary>The start of MIB_IF_ROW2 up to the link speeds, then the counters as one block.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu;
        public uint Type;
        public uint TunnelType;
        public uint MediaType;
        public uint PhysicalMediumType;
        public uint AccessType;
        public uint DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public uint OperStatus;
        public uint AdminStatus;
        public uint MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public fixed ulong Counters[18];
    }

    public readonly record struct InterfaceRow(bool HardwareInterface, bool MediaConnected, ulong LinkSpeed);

    /// <summary>The row of an interface by its GUID, or null when Windows does not know it.</summary>
    public static InterfaceRow? GetInterface(Guid interfaceGuid)
    {
        var row = default(MIB_IF_ROW2);
        if (ConvertInterfaceGuidToLuid(&interfaceGuid, &row.InterfaceLuid) != 0 || GetIfEntry2(&row) != 0)
            return null;
        // Flags: bit 0 HardwareInterface, bit 1 FilterInterface. MediaConnectState 1 is connected.
        var flags = row.InterfaceAndOperStatusFlags;
        return new InterfaceRow((flags & 1) != 0 && (flags & 2) == 0, row.MediaConnectState == 1, Math.Max(row.ReceiveLinkSpeed, row.TransmitLinkSpeed));
    }

    [LibraryImport("iphlpapi.dll")]
    private static partial uint ConvertInterfaceGuidToLuid(Guid* interfaceGuid, ulong* interfaceLuid);

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetIfEntry2(MIB_IF_ROW2* row);

    // wlanapi: interface states, saved profiles and change notifications.

    public enum WlanInterfaceState
    {
        NotReady = 0,
        Connected = 1,
        AdHocNetworkFormed = 2,
        Disconnecting = 3,
        Disconnected = 4,
        Associating = 5,
        Discovering = 6,
        Authenticating = 7,
    }

    private static readonly object HandleLock = new();
    private static nint _handle;
    private static bool _opened;
    private static Action? _changed;

    /// <summary>The wlanapi client handle, opened once for the process. Zero when there is no WLAN service.</summary>
    private static nint Handle
    {
        get
        {
            lock (HandleLock)
            {
                if (_opened)
                    return _handle;
                _opened = true;
                try
                {
                    if (WlanOpenHandle(2, 0, out _, out var handle) == 0)
                        _handle = handle;
                }
                catch (DllNotFoundException)
                {
                }
                return _handle;
            }
        }
    }

    /// <summary>Calls <paramref name="changed"/> on a wlanapi thread when a WLAN connection or scan changes.</summary>
    public static void Listen(Action changed)
    {
        _changed = changed;
        var handle = Handle;
        if (handle == 0)
            return;
        // ACM avoids the location consent required by MSM notifications.
        _ = WlanRegisterNotification(handle, 0x08, 1, &OnNotification, 0, 0, out _);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnNotification(nint data, nint context) =>
        NativeCallback.Guard(Log.Default, "network", () => _changed?.Invoke());

    /// <summary>The state of each WLAN interface by its GUID.</summary>
    public static Dictionary<Guid, WlanInterfaceState> InterfaceStates()
    {
        var result = new Dictionary<Guid, WlanInterfaceState>();
        var handle = Handle;
        if (handle == 0 || WlanEnumInterfaces(handle, 0, out var list) != 0)
            return result;
        try
        {
            var count = *(uint*)list;
            // WLAN_INTERFACE_INFO_LIST: two DWORDs, then WLAN_INTERFACE_INFO { GUID; WCHAR[256]; state }.
            var item = (byte*)list + 8;
            for (var i = 0; i < count; i++, item += 16 + 512 + 4)
                result[*(Guid*)item] = (WlanInterfaceState)(*(int*)(item + 16 + 512));
        }
        finally
        {
            WlanFreeMemory(list);
        }
        return result;
    }

    /// <summary>The names of the saved WLAN profiles of an interface.</summary>
    public static HashSet<string> ProfileNames(Guid interfaceGuid)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var handle = Handle;
        if (handle == 0 || WlanGetProfileList(handle, &interfaceGuid, 0, out var list) != 0)
            return result;
        try
        {
            var count = *(uint*)list;
            // WLAN_PROFILE_INFO_LIST: two DWORDs, then WLAN_PROFILE_INFO { WCHAR[256]; DWORD flags }.
            var item = (byte*)list + 8;
            for (var i = 0; i < count; i++, item += 512 + 4)
                result.Add(new string((char*)item));
        }
        finally
        {
            WlanFreeMemory(list);
        }
        return result;
    }

    // wlanapi.h: wlan_intf_opcode_autoconf_start is 0, so wlan_intf_opcode_current_operation_mode is 12.
    // ppData is a ULONG, one DOT11_OPERATION_MODE_* value. wlan_intf_opcode_bss_type is not queried:
    // Windows 11 can answer it with dot11_BSS_type_any for a connected station, which does not
    // separate an ad-hoc network from infrastructure, and IBSS is unsupported. AdHoc stays unreached.
    private const uint OpcodeCurrentOperationMode = 12;

    /// <summary>
    /// The interface operation mode, or null when Windows does not answer. A failure does not throw.
    /// The buffer <c>WlanQueryInterface</c> returns is freed on every path, including failure.
    /// </summary>
    public static uint? OperationMode(Guid interfaceGuid)
    {
        var handle = Handle;
        if (handle == 0)
            return null;
        nint data = 0;
        try
        {
            uint size = 0;
            if (WlanQueryInterface(handle, &interfaceGuid, OpcodeCurrentOperationMode, 0, &size, &data, null) != 0
                || data == 0 || size < sizeof(uint))
                return null;
            return *(uint*)data;
        }
#pragma warning disable CA1031 // A failed mode query is Unknown and must not fail the snapshot.
        catch (Exception e) when (e is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return null;
        }
        finally
        {
            if (data != 0)
                WlanFreeMemory(data);
        }
    }

    /// <summary>Deletes a saved WLAN profile. Returns the Windows error, or 0.</summary>
    public static uint DeleteProfile(Guid interfaceGuid, string profileName)
    {
        var handle = Handle;
        if (handle == 0)
            return 1062; // ERROR_SERVICE_NOT_ACTIVE
        return WlanDeleteProfile(handle, &interfaceGuid, profileName, 0);
    }

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiatedVersion, out nint clientHandle);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanEnumInterfaces(nint clientHandle, nint reserved, out nint interfaceList);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanGetProfileList(nint clientHandle, Guid* interfaceGuid, nint reserved, out nint profileList);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanQueryInterface(
        nint clientHandle, Guid* interfaceGuid, uint opCode, nint reserved, uint* dataSize, nint* data, uint* opcodeValueType);

    [LibraryImport("wlanapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint WlanDeleteProfile(nint clientHandle, Guid* interfaceGuid, string profileName, nint reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanRegisterNotification(
        nint clientHandle, uint notificationSource, int ignoreDuplicate, delegate* unmanaged[Stdcall]<nint, nint, void> callback,
        nint context, nint reserved, out uint previousSource);

    [LibraryImport("wlanapi.dll")]
    private static partial void WlanFreeMemory(nint memory);
}
