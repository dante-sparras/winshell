using System.Runtime.InteropServices;

namespace Winshell.Platform.Windows.Services.Audio;

/// <summary>
/// Moves a COM object onto the thread that will use it. Core Audio callbacks arrive on the engine's
/// thread, and the shell reads those objects on its own thread.
/// </summary>
internal static partial class ComApartment
{
    private static readonly Guid IUnknown = new("00000000-0000-0000-C000-000000000046");

    /// <summary>A stream the receiving thread releases by restoring it. The caller still owns <paramref name="comObject"/>.</summary>
    public static nint Capture(object comObject)
    {
        var unknown = Marshal.GetIUnknownForObject(comObject);
        try
        {
            var iid = IUnknown;
            Marshal.ThrowExceptionForHR(CoMarshalInterThreadInterfaceInStream(ref iid, unknown, out var stream));
            return stream;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>The object on this thread. Releases <paramref name="stream"/>, including when the restore fails.</summary>
    public static object Restore(nint stream)
    {
        var iid = IUnknown;
        Marshal.ThrowExceptionForHR(CoGetInterfaceAndReleaseStream(stream, ref iid, out var unknown));
        try
        {
            return Marshal.GetObjectForIUnknown(unknown);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [LibraryImport("ole32")]
    private static partial int CoMarshalInterThreadInterfaceInStream(ref Guid iid, nint unknown, out nint stream);

    [LibraryImport("ole32")]
    private static partial int CoGetInterfaceAndReleaseStream(nint stream, ref Guid iid, out nint unknown);
}
