using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Winshell.Native;

/// <summary>
/// Moves a COM object onto the thread that will use it. Core Audio callbacks arrive on the engine's
/// thread, and the shell reads those objects on its own thread.
/// </summary>
internal static unsafe class ComApartment
{
    /// <summary>A stream the receiving thread releases by restoring it. The caller still owns <paramref name="comObject"/>.</summary>
    public static nint Capture(object comObject)
    {
        var unknown = Marshal.GetIUnknownForObject(comObject);
        try
        {
            IStream* stream;
            PInvoke.CoMarshalInterThreadInterfaceInStream(typeof(IUnknown).GUID, (IUnknown*)unknown, &stream)
                .ThrowOnFailure();
            return (nint)stream;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>The object on this thread. Releases <paramref name="stream"/>, including when the restore fails.</summary>
    public static object Restore(nint stream)
    {
        PInvoke.CoGetInterfaceAndReleaseStream((IStream*)stream, out IUnknown* unknown).ThrowOnFailure();
        return Com.Wrap<object>((nint)unknown) ?? throw new InvalidComObjectException();
    }
}
