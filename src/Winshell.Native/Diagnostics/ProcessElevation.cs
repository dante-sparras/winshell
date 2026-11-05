using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>Whether a process's token is elevated.</summary>
internal static unsafe class ProcessElevation
{
    private const int TokenElevation = 20;

    /// <summary>
    /// Null when the process or its token cannot be read. <paramref name="tokenDenied"/> says the process opened but
    /// its token was refused.
    /// </summary>
    internal static bool? TryRead(uint processId, out bool tokenDenied)
    {
        tokenDenied = false;
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
            return null;
        try
        {
            if (!Win32.OpenProcessToken(process, Win32.TOKEN_QUERY, out var token))
            {
                tokenDenied = Marshal.GetLastPInvokeError() == Win32.ERROR_ACCESS_DENIED;
                return null;
            }

            try
            {
                int elevated;
                return Win32.GetTokenInformation(token, TokenElevation, &elevated, sizeof(int), out _)
                    ? elevated != 0
                    : null;
            }
            finally
            {
                Win32.CloseHandle(token);
            }
        }
        finally
        {
            Win32.CloseHandle(process);
        }
    }
}
