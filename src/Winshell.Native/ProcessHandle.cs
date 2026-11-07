using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace Winshell.Native;

/// <summary>How a read of a process's token ended.</summary>
internal enum TokenRead
{
    Read,

    /// <summary>The process opened, but Windows refused its token.</summary>
    Denied,
    Failed
}

/// <summary>A process opened for limited queries. Disposing it closes the handle.</summary>
internal readonly unsafe ref struct ProcessHandle
{
    private ProcessHandle(HANDLE handle)
    {
        Handle = handle;
    }

    internal HANDLE Handle { get; }

    /// <summary>False when the process could not be opened.</summary>
    internal bool IsOpen => !Handle.IsNull;

    internal static ProcessHandle Open(uint processId)
    {
        return new ProcessHandle(
            PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId));
    }

    /// <summary>
    /// Reads one class of the process's token into <paramref name="buffer"/>. A process that did not open fails.
    /// </summary>
    internal TokenRead ReadToken(TOKEN_INFORMATION_CLASS info, Span<byte> buffer)
    {
        if (!IsOpen)
            return TokenRead.Failed;
        HANDLE token;
        if (!PInvoke.OpenProcessToken(Handle, TOKEN_ACCESS_MASK.TOKEN_QUERY, &token))
            return (WIN32_ERROR)Marshal.GetLastPInvokeError() == WIN32_ERROR.ERROR_ACCESS_DENIED
                ? TokenRead.Denied
                : TokenRead.Failed;
        try
        {
            uint returned;
            fixed (byte* into = buffer)
            {
                return PInvoke.GetTokenInformation(token, info, into, (uint)buffer.Length, &returned)
                    ? TokenRead.Read
                    : TokenRead.Failed;
            }
        }
        finally
        {
            PInvoke.CloseHandle(token);
        }
    }

    public void Dispose()
    {
        if (IsOpen)
            PInvoke.CloseHandle(Handle);
    }
}
