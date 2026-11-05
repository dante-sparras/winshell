using System.Runtime.InteropServices;
using Windows.Win32;

namespace Winshell.Native;

internal static class KnownFolder
{
    /// <summary>The path of a known folder, or null when Windows has no folder for <paramref name="id"/>.</summary>
    internal static unsafe string? Find(Guid id)
    {
        var hr = PInvoke.SHGetKnownFolderPath(id, default, default, out var path);
        try
        {
            return hr.Succeeded ? path.ToString() : null;
        }
        finally
        {
            // The caller frees the buffer whether the call succeeded or not.
            Marshal.FreeCoTaskMem((nint)path.Value);
        }
    }
}
