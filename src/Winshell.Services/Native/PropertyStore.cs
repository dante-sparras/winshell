using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>Reads typed values from a Windows property store: an audio device's, a window's or a shortcut's.</summary>
internal static partial class PropertyStore
{
    private const ushort VtUI4 = 19;
    private const ushort VtLpwstr = 31;
    private const ushort VtBlob = 65;

    /// <summary>A string value, or null when the store has none of that key or holds another type.</summary>
    public static string? ReadString(IPropertyStore store, in PROPERTYKEY key)
    {
        if (store.GetValue(key, out var value) != 0)
            return null;
        try
        {
            return value.Type == VtLpwstr ? Marshal.PtrToStringUni(value.Value) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary>A 32-bit unsigned value, or null when the store has none of that key or holds another type.</summary>
    public static uint? ReadUInt(IPropertyStore store, in PROPERTYKEY key)
    {
        if (store.GetValue(key, out var value) != 0)
            return null;
        try
        {
            return value.Type == VtUI4 ? (uint)value.Value : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary>A copy of a blob value, or null when the store has none of that key or holds another type.</summary>
    public static byte[]? ReadBlob(IPropertyStore store, in PROPERTYKEY key)
    {
        if (store.GetValue(key, out var value) != 0)
            return null;
        try
        {
            if (value.Type != VtBlob || value.Extra == 0)
                return null;
            var bytes = new byte[(uint)value.Value];
            Marshal.Copy(value.Extra, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PROPVARIANT value);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PROPERTYKEY(Guid format, uint id)
{
    public readonly Guid Format = format;
    public readonly uint Id = id;
}

/// <summary>
/// A PROPVARIANT. The union after the type is read as two pointer-sized halves: a string is the first, a 32-bit
/// number is the low half of the first, and a BLOB is its size in the first and its data in the second, on 64-bit
/// and 32-bit Windows alike.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort Type;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint Value;
    public nint Extra;
}

/// <summary>IPropertyStore, with the Windows SDK's methods in its vtable order.</summary>
[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PROPERTYKEY key);

    [PreserveSig]
    int GetValue(in PROPERTYKEY key, out PROPVARIANT value);

    [PreserveSig]
    int SetValue(in PROPERTYKEY key, in PROPVARIANT value);

    [PreserveSig]
    int Commit();
}
