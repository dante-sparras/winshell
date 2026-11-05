using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.WiFi;

namespace Winshell.Native;

/// <summary>A WLAN interface state, as <c>WLAN_INTERFACE_STATE</c> numbers it.</summary>
internal enum WlanInterfaceState
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

/// <summary>The parts of wlanapi that WinRT does not cover: interface states, saved profiles and change notifications.</summary>
internal static unsafe class Wlan
{
    private static readonly object HandleLock = new();
    private static HANDLE _handle;
    private static bool _opened;
    private static Action? _changed;

    /// <summary>The wlanapi client handle, opened once for the process. Null when there is no WLAN service.</summary>
    private static HANDLE Handle
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
                    if (PInvoke.WlanOpenHandle(2, out _, out var handle) == 0)
                        _handle = handle;
                }
                catch (DllNotFoundException)
                {
                }
                return _handle;
            }
        }
    }

    /// <summary>
    /// Calls <paramref name="changed"/> on a wlanapi thread when a WLAN connection or scan changes.
    /// An exception it throws stops here, because it must not reach wlanapi; <paramref name="changed"/> reports its own failures.
    /// </summary>
    internal static void Listen(Action changed)
    {
        _changed = changed;
        var handle = Handle;
        if (handle.IsNull)
            return;
        // ACM avoids the location consent required by MSM notifications.
        _ = PInvoke.WlanRegisterNotification(handle, WLAN_NOTIFICATION_SOURCES.WLAN_NOTIFICATION_SOURCE_ACM, true, &OnNotification, null);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnNotification(L2_NOTIFICATION_DATA* data, void* context)
    {
#pragma warning disable CA1031 // wlanapi called in; nothing above this frame can catch it.
        try
        {
            _changed?.Invoke();
        }
        catch (Exception)
        {
        }
#pragma warning restore CA1031
    }

    /// <summary>The state of each WLAN interface by its GUID.</summary>
    internal static Dictionary<Guid, WlanInterfaceState> InterfaceStates()
    {
        var result = new Dictionary<Guid, WlanInterfaceState>();
        var handle = Handle;
        if (handle.IsNull || PInvoke.WlanEnumInterfaces(handle, out var list) != 0)
            return result;
        try
        {
            foreach (ref readonly var item in list->InterfaceInfo.AsSpan((int)list->dwNumberOfItems))
                result[item.InterfaceGuid] = (WlanInterfaceState)item.isState;
        }
        finally
        {
            PInvoke.WlanFreeMemory(list);
        }
        return result;
    }

    /// <summary>The names of the saved WLAN profiles of an interface.</summary>
    internal static HashSet<string> ProfileNames(Guid interfaceGuid)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var handle = Handle;
        if (handle.IsNull || PInvoke.WlanGetProfileList(handle, interfaceGuid, out var list) != 0)
            return result;
        try
        {
            foreach (ref readonly var item in list->ProfileInfo.AsSpan((int)list->dwNumberOfItems))
                result.Add(item.strProfileName.ToString());
        }
        finally
        {
            PInvoke.WlanFreeMemory(list);
        }
        return result;
    }

    /// <summary>
    /// The interface operation mode, one <c>DOT11_OPERATION_MODE_*</c> value, or null when Windows does not answer.
    /// A failure does not throw. The buffer <c>WlanQueryInterface</c> returns is freed on every path, including failure.
    /// </summary>
    /// <remarks>
    /// <c>wlan_intf_opcode_bss_type</c> is not queried: Windows 11 can answer it with <c>dot11_BSS_type_any</c> for a
    /// connected station, which does not separate an ad-hoc network from infrastructure, and IBSS is unsupported.
    /// </remarks>
    internal static uint? OperationMode(Guid interfaceGuid)
    {
        var handle = Handle;
        if (handle.IsNull)
            return null;
        void* data = null;
        try
        {
            if (PInvoke.WlanQueryInterface(handle, interfaceGuid, WLAN_INTF_OPCODE.wlan_intf_opcode_current_operation_mode, out var size, out data) != 0
                || data == null || size < sizeof(uint))
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
            if (data != null)
                PInvoke.WlanFreeMemory(data);
        }
    }

    /// <summary>Deletes a saved WLAN profile. Returns the Windows error, or 0.</summary>
    internal static uint DeleteProfile(Guid interfaceGuid, string profileName)
    {
        var handle = Handle;
        if (handle.IsNull)
            return (uint)WIN32_ERROR.ERROR_SERVICE_NOT_ACTIVE;
        return PInvoke.WlanDeleteProfile(handle, interfaceGuid, profileName);
    }

    /// <summary>The only flags a profile read passes in. Zero, so the call never asks Windows for a key.</summary>
    internal static uint ProfileReadFlags() => 0;

    /// <summary>Per-user and group-policy bits. A plaintext-key bit is not one of them and is not kept.</summary>
    internal const uint ProfileFlagMask = PInvoke.WLAN_PROFILE_USER | PInvoke.WLAN_PROFILE_GROUP_POLICY;

    internal static uint ProfileWriteFlags(uint flags) => flags & ProfileFlagMask;

    /// <summary>
    /// The profile XML Windows stored. <see cref="ProfileReadFlags"/> is the only value passed in, so the key stays out.
    /// The returned flags are only the per-user and group-policy bits.
    /// </summary>
    internal static uint GetProfile(Guid interfaceGuid, string profileName, out string? xml, out uint flags)
    {
        xml = null;
        flags = 0;
        var handle = Handle;
        if (handle.IsNull)
            return (uint)WIN32_ERROR.ERROR_SERVICE_NOT_ACTIVE;
        var requested = ProfileReadFlags();
        var error = PInvoke.WlanGetProfile(handle, interfaceGuid, profileName, out var text, ref requested, out _);
        if (text.Value == null)
            return error == 0 ? (uint)WIN32_ERROR.ERROR_NOT_FOUND : error;
        try
        {
            if (error != 0)
                return error;
            xml = text.ToString();
            flags = ProfileWriteFlags(requested);
            return 0;
        }
        finally
        {
            PInvoke.WlanFreeMemory(text.Value);
        }
    }

    /// <summary>Replaces a profile, keeping only its per-user or group-policy flag. Returns the Windows error, or 0.</summary>
    internal static uint SetProfile(Guid interfaceGuid, string profileXml, uint flags, out uint reason)
    {
        reason = 0;
        var handle = Handle;
        if (handle.IsNull)
            return (uint)WIN32_ERROR.ERROR_SERVICE_NOT_ACTIVE;
        if (string.IsNullOrEmpty(profileXml))
            return (uint)WIN32_ERROR.ERROR_INVALID_PARAMETER;
        return PInvoke.WlanSetProfile(handle, interfaceGuid, ProfileWriteFlags(flags), profileXml, null, true, out reason);
    }

    /// <summary>Connects with a saved profile (<c>wlan_connection_mode_profile</c>). Returns when Windows accepts the request.</summary>
    internal static uint ConnectProfile(Guid interfaceGuid, string profileName, bool hidden)
    {
        var handle = Handle;
        if (handle.IsNull)
            return (uint)WIN32_ERROR.ERROR_SERVICE_NOT_ACTIVE;
        if (string.IsNullOrEmpty(profileName))
            return (uint)WIN32_ERROR.ERROR_INVALID_PARAMETER;
        fixed (char* name = profileName)
        {
            var parameters = new WLAN_CONNECTION_PARAMETERS
            {
                wlanConnectionMode = WLAN_CONNECTION_MODE.wlan_connection_mode_profile,
                strProfile = name,
                dot11BssType = DOT11_BSS_TYPE.dot11_BSS_type_any,
                dwFlags = hidden ? PInvoke.WLAN_CONNECTION_HIDDEN_NETWORK : 0,
            };
            return PInvoke.WlanConnect(handle, interfaceGuid, parameters);
        }
    }
}
