using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Winshell;

/// <summary>The exceptions a Windows API that refuses or fails is turned into.</summary>
internal static class WindowsFailure
{
    /// <summary>
    /// Whether <paramref name="e"/> is Windows saying no rather than a bug. WinRT turns <c>E_INVALIDARG</c> into
    /// <see cref="ArgumentException"/> and <c>E_ILLEGAL_METHOD_CALL</c> into <see cref="InvalidOperationException"/>,
    /// and a missing device or file into an <see cref="IOException"/>.
    /// </summary>
    public static bool IsExpected(Exception e)
    {
        return e is UnauthorizedAccessException or COMException or Win32Exception or IOException
            or InvalidOperationException or NotSupportedException or ArgumentException;
    }
}
