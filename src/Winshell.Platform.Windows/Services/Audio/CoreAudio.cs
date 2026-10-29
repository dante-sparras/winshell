using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;

namespace Winshell.Platform.Windows.Services.Audio;

// The Windows Core Audio COM interfaces the audio service uses, declared for classic COM interop. Methods the
// service never calls keep their place in the vtable with a placeholder signature.

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid format, uint id)
{
    public Guid Format = format;
    public uint Id = id;
}

/// <summary>A PROPVARIANT, read for the few types the service needs: strings, numbers and blobs (64-bit layout).</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public nint Pointer;
    [FieldOffset(8)] public uint UInt;
    [FieldOffset(8)] public uint BlobSize;
    [FieldOffset(16)] public nint BlobData;

    public const ushort VtUI4 = 19;
    public const ushort VtLpwstr = 31;
    public const ushort VtBlob = 65;
}

internal static class CoreAudio
{
    public const uint DeviceStateActive = 1;
    public const uint ClsctxAll = 0x17;

    public static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid PolicyConfigClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    public static readonly Guid AudioEndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static readonly Guid AudioMeterInformationIid = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    public static readonly Guid AudioSessionManager2Iid = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static readonly Guid AudioClientIid = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

    public static readonly PropertyKey DeviceFriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    public static readonly PropertyKey DeviceDescription = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);
    public static readonly PropertyKey InterfaceFriendlyName = new(new Guid("026E516E-B814-414B-83CD-856D6FEF4822"), 2);
    public static readonly PropertyKey EndpointFormFactor = new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);
    public static readonly PropertyKey EngineDeviceFormat = new(new Guid("F19F064D-082C-4E27-BC73-6882A1BB8E4C"), 0);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant value);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHLoadIndirectString(string source, char[] output, int outputLength, nint reserved);

    public static T Create<T>(Guid clsid)
    {
        var type = Type.GetTypeFromCLSID(clsid, true)!;
        return (T)Activator.CreateInstance(type)!;
    }

    /// <summary>Releases a COM object's callable wrapper now, not when the GC finds it. Does nothing for null.</summary>
    public static void Release(object? comObject)
    {
        if (comObject is not null)
            Marshal.FinalReleaseComObject(comObject);
    }

    public static T Activate<T>(IMMDevice device, Guid iid)
    {
        device.Activate(ref iid, ClsctxAll, 0, out var obj);
        return (T)obj;
    }

    public static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0)
            return null;
        try
        {
            return value.Type == PropVariant.VtLpwstr ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    public static uint? ReadUInt(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0)
            return null;
        try
        {
            return value.Type == PropVariant.VtUI4 ? value.UInt : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    public static byte[]? ReadBlob(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0)
            return null;
        try
        {
            if (value.Type != PropVariant.VtBlob || value.BlobData == 0)
                return null;
            var bytes = new byte[value.BlobSize];
            Marshal.Copy(value.BlobData, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary>Resolves a string like <c>@%SystemRoot%\System32\AudioSrv.Dll,-202</c> to its text.</summary>
    public static string LoadIndirectString(string text)
    {
        if (!text.StartsWith('@'))
            return text;
        var buffer = new char[512];
        return SHLoadIndirectString(text, buffer, buffer.Length, 0) == 0 ? new string(buffer).TrimEnd('\0') : "";
    }

    /// <summary>The full path of a process's program, or null when the process cannot be opened or the name cannot be read.</summary>
    public static string? ProcessImagePath(uint processId) => ProcessImage.TryRead(processId);
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow flow, uint stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    void RegisterEndpointNotificationCallback(IMMNotificationClient client);

    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint count);

    void Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, uint clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    void OpenPropertyStore(uint access, out IPropertyStore properties);

    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    void GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);

    void GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);

    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    void GetChannelCount(out uint count);

    void SetMasterVolumeLevel(float levelDb, ref Guid context);

    void SetMasterVolumeLevelScalar(float level, ref Guid context);

    void GetMasterVolumeLevel(out float levelDb);

    void GetMasterVolumeLevelScalar(out float level);

    void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);

    void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

    void GetChannelVolumeLevel(uint channel, out float levelDb);

    void GetChannelVolumeLevelScalar(uint channel, out float level);

    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig]
    int OnNotify(nint data);
}

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float peak);

    [PreserveSig]
    int GetMeteringChannelCount(out uint count);

    [PreserveSig]
    int GetChannelsPeakValues(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peaks);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint frames);

    [PreserveSig]
    int GetStreamLatency(out long latency);

    [PreserveSig]
    int GetCurrentPadding(out uint frames);

    [PreserveSig]
    int IsFormatSupported(int shareMode, nint format, out nint closest);

    [PreserveSig]
    int GetMixFormat(out nint format);

    [PreserveSig]
    int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(nint sessionGuid, uint flags, out IAudioSessionControl2 control);

    void GetSimpleAudioVolume(nint sessionGuid, uint flags, out ISimpleAudioVolume volume);

    void GetSessionEnumerator(out IAudioSessionEnumerator sessions);

    void RegisterSessionNotification(IAudioSessionNotification notification);

    void UnregisterSessionNotification(IAudioSessionNotification notification);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int count);

    void GetSession(int index, out IAudioSessionControl2 session);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    void GetState(out AudioSessionState state);

    void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);

    void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);

    void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);

    void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);

    void GetGroupingParam(out Guid param);

    void SetGroupingParam(ref Guid param, ref Guid context);

    void RegisterAudioSessionNotification(IAudioSessionEvents events);

    void UnregisterAudioSessionNotification(IAudioSessionEvents events);

    // IAudioSessionControl2
    void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);

    void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);

    void GetProcessId(out uint processId);

    [PreserveSig]
    int IsSystemSoundsSession();
}

[ComImport]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig]
    int OnSessionCreated(IAudioSessionControl2 session);
}

[ComImport]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig]
    int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);

    [PreserveSig]
    int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);

    [PreserveSig]
    int OnSimpleVolumeChanged(float volume, [MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

    [PreserveSig]
    int OnChannelVolumeChanged(uint count, nint volumes, uint changedChannel, ref Guid context);

    [PreserveSig]
    int OnGroupingParamChanged(ref Guid param, ref Guid context);

    [PreserveSig]
    int OnStateChanged(AudioSessionState state);

    [PreserveSig]
    int OnSessionDisconnected(int reason);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid context);

    void GetMasterVolume(out float level);

    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

/// <summary>
/// The undocumented policy interface Windows' own sound settings use to change the default device. It has been
/// stable since Windows 7; when it is missing, setting the default device does nothing.
/// </summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    void GetMixFormat();

    void GetDeviceFormat();

    void ResetDeviceFormat();

    void SetDeviceFormat();

    void GetProcessingPeriod();

    void SetProcessingPeriod();

    void GetShareMode();

    void SetShareMode();

    void GetPropertyValue();

    void SetPropertyValue();

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
}
