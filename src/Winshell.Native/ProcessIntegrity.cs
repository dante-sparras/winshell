using Windows.Win32;
using Windows.Win32.Security;

namespace Winshell.Native;

/// <summary>
/// A process's mandatory integrity level: the last sub-authority of its token's label SID, S-1-16-level. Low is
/// 0x1000, medium 0x2000, high 0x3000 and system 0x4000.
/// </summary>
internal static unsafe class ProcessIntegrity
{
    internal const uint Medium = 0x2000;

    /// <summary>The level, or null when the process cannot be opened or its token read.</summary>
    internal static uint? TryRead(uint processId)
    {
        using var process = ProcessHandle.Open(processId);
        // A TOKEN_MANDATORY_LABEL, followed by the label's SID.
        Span<byte> label = stackalloc byte[64];
        if (process.ReadToken(TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, label) != TokenRead.Read)
            return null;
        fixed (byte* start = label)
        {
            var sid = new PSID(*(void**)start);
            var count = PInvoke.GetSidSubAuthorityCount(sid);
            if (count == null || *count == 0)
                return null;
            var level = PInvoke.GetSidSubAuthority(sid, (uint)(*count - 1));
            return level == null ? null : *level;
        }
    }
}
