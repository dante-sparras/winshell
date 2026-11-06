using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;

namespace Winshell.Native;

internal static class ClipboardImage
{
    /// <summary>
    /// Replaces the clipboard's contents with <paramref name="dib"/>, a BITMAPV5HEADER and its pixels, as CF_DIBV5.
    /// <paramref name="owner"/> is the window that opens the clipboard. Throws a <see cref="Win32Exception"/> when
    /// Windows refuses.
    /// </summary>
    internal static void SetDibV5(byte[] dib, nint owner)
    {
        var memory = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (nuint)dib.Length);
        if (memory == 0)
            throw new Win32Exception();
        try
        {
            var pointer = Win32.GlobalLock(memory);
            if (pointer == 0)
                throw new Win32Exception();
            try
            {
                Marshal.Copy(dib, 0, pointer, dib.Length);
            }
            finally
            {
                Win32.GlobalUnlock(memory);
            }

            if (!Win32.OpenClipboard(owner))
                throw new Win32Exception();
            try
            {
                if (!Win32.EmptyClipboard() || Win32.SetClipboardData((uint)CLIPBOARD_FORMAT.CF_DIBV5, memory) == 0)
                    throw new Win32Exception();
                memory = 0; // The clipboard owns the allocation now.
            }
            finally
            {
                Win32.CloseClipboard();
            }
        }
        finally
        {
            if (memory != 0)
                PInvoke.GlobalFree(new HGLOBAL(memory));
        }
    }
}
