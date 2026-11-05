using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>A Windows power request that keeps the screen on and the PC awake until it is disposed.</summary>
internal sealed class PowerRequest : IDisposable
{
    private const int DisplayRequired = 0;
    private const int SystemRequired = 1;

    private nint _handle;

    private PowerRequest(nint handle) => _handle = handle;

    /// <summary>
    /// Keeps the screen on and the PC awake for <paramref name="reason"/>. Null when Windows refuses:
    /// <paramref name="failedStep"/> is <c>create</c> or <c>set</c>, and <paramref name="error"/> is the Windows error.
    /// </summary>
    internal static unsafe PowerRequest? TryCreate(string reason, out string failedStep, out int error)
    {
        nint handle;
        fixed (char* text = reason)
        {
            var context = new REASON_CONTEXT { Version = 0, Flags = 1, SimpleReasonString = (nint)text };
            handle = Win32.PowerCreateRequest(ref context);
        }
        if (handle == -1 || handle == 0)
        {
            (failedStep, error) = ("create", Marshal.GetLastPInvokeError());
            return null;
        }
        if (!Win32.PowerSetRequest(handle, DisplayRequired) || !Win32.PowerSetRequest(handle, SystemRequired))
        {
            (failedStep, error) = ("set", Marshal.GetLastPInvokeError());
            Win32.CloseHandle(handle);
            return null;
        }
        (failedStep, error) = ("", 0);
        return new PowerRequest(handle);
    }

    /// <summary>Clears the request, so it no longer keeps the PC awake.</summary>
    public void Dispose()
    {
        if (_handle == 0)
            return;
        Win32.PowerClearRequest(_handle, DisplayRequired);
        Win32.PowerClearRequest(_handle, SystemRequired);
        Win32.CloseHandle(_handle);
        _handle = 0;
    }
}
