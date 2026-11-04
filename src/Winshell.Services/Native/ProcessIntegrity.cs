using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// A process's mandatory integrity level: the last sub-authority of its token's label SID, S-1-16-level. Low is
/// 0x1000, medium 0x2000, high 0x3000 and system 0x4000.
/// </summary>
internal static unsafe partial class ProcessIntegrity
{
    internal const uint Medium = 0x2000;

    private const uint QueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    /// <summary>The level, or null when the process cannot be opened or its token read.</summary>
    internal static uint? TryRead(uint processId)
    {
        var process = Win32.OpenProcess(QueryLimitedInformation, false, processId);
        if (process == 0)
            return null;
        try
        {
            if (!Win32.OpenProcessToken(process, TokenQuery, out var token))
                return null;
            try
            {
                // A TOKEN_MANDATORY_LABEL, followed by the label's SID.
                var label = stackalloc byte[64];
                if (!Win32.GetTokenInformation(token, TokenIntegrityLevel, label, 64, out _))
                    return null;
                var sid = *(nint*)label;
                var count = GetSidSubAuthorityCount(sid);
                if (count == null || *count == 0)
                    return null;
                var level = GetSidSubAuthority(sid, (uint)(*count - 1));
                return level == null ? null : *level;
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

    [LibraryImport("advapi32.dll")]
    private static partial byte* GetSidSubAuthorityCount(nint sid);

    [LibraryImport("advapi32.dll")]
    private static partial uint* GetSidSubAuthority(nint sid, uint index);
}
