using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace Winshell.Native;

/// <summary>Whether a path is a symbolic link, and how many hard links a file has.</summary>
internal static unsafe class FileLinks
{
    /// <summary>
    /// True when <paramref name="path"/> could be queried. <paramref name="reparse"/> is true for a symbolic link
    /// and false for a normal file or a hard link. A missing file returns false.
    /// </summary>
    internal static bool TryReparsePoint(string path, out bool reparse)
    {
        reparse = false;
        var handle = PInvoke.FindFirstFile(path, out var data);
        if ((nint)handle.Value == -1)
            return false;
        try
        {
            reparse = (data.dwFileAttributes & (uint)FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_REPARSE_POINT) != 0;
            return true;
        }
        finally
        {
            PInvoke.FindClose(handle);
        }
    }

    /// <summary>The number of hard links to an open file, or null when Windows could not say.</summary>
    internal static int? Count(SafeFileHandle file)
    {
        return PInvoke.GetFileInformationByHandle(new HANDLE(file.DangerousGetHandle()), out var info)
            ? (int)info.nNumberOfLinks
            : null;
    }
}
