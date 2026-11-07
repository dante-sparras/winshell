using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Winshell;

/// <summary>Tells a Windows API that refused or failed apart from a bug.</summary>
internal static class WindowsFailure
{
    /// <summary>
    /// Whether <paramref name="e"/> is what a failure <c>HRESULT</c> or Win32 error becomes. WinRT maps
    /// <c>E_ACCESSDENIED</c>, <c>E_INVALIDARG</c>, <c>E_ILLEGAL_METHOD_CALL</c> and missing-file codes to their .NET
    /// exceptions and most other codes to <see cref="COMException"/>, and a failed Win32 call reports a
    /// <see cref="Win32Exception"/>. Services throw <see cref="NotSupportedException"/> and
    /// <see cref="InvalidOperationException"/> themselves when Windows refuses without an exception.
    /// </summary>
    public static bool IsExpected(Exception e)
    {
        return e is UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or NotSupportedException or IOException or COMException or Win32Exception;
    }
}
