using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>What a failed file operation says about the file.</summary>
internal static class FileErrors
{
    /// <summary>
    /// True when another process held the file: access was denied, or the file was shared or locked against it.
    /// Antivirus and indexers do this to a file that was just written.
    /// </summary>
    internal static bool IsLock(Exception exception)
    {
        var code = (WIN32_ERROR)(exception.HResult & 0xFFFF);
        return code is WIN32_ERROR.ERROR_ACCESS_DENIED or WIN32_ERROR.ERROR_SHARING_VIOLATION
            or WIN32_ERROR.ERROR_LOCK_VIOLATION;
    }
}
