using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.KernelStreaming;

namespace Winshell.Native;

/// <summary>The one-shot connection requests of a Bluetooth audio driver.</summary>
internal static class BluetoothAudio
{
    /// <summary>KSPROPSETID_BtAudio. CsWin32 emits it as a class to create, not as a GUID value.</summary>
    private static readonly Guid PropertySet = new("7fa06c40-b8f6-4c7e-8556-e8c33a12e54d");

    /// <summary>Asks the audio filter at <paramref name="filterPath"/> to connect or disconnect. Whether the driver accepted the request.</summary>
    internal static unsafe bool SetConnection(string filterPath, bool connect)
    {
        using var device = Win32.CreateFileW(filterPath, 0xC0000000, 3, 0, 3, 0, 0);
        if (device.IsInvalid)
            return false;
        var request = new KSIDENTIFIER();
        request.Set = PropertySet;
        request.Id = (uint)(connect
            ? KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_RECONNECT
            : KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_DISCONNECT);
        request.Flags = PInvoke.KSPROPERTY_TYPE_GET;
        uint returned;
        return PInvoke.DeviceIoControl(new HANDLE(device.DangerousGetHandle()), PInvoke.IOCTL_KS_PROPERTY,
            &request, (uint)sizeof(KSIDENTIFIER), null, 0, &returned, null);
    }
}
