using Windows.Win32.Security;

namespace Winshell.Native;

/// <summary>Whether a process's token is elevated.</summary>
internal static class ProcessElevation
{
    /// <summary>
    /// Null when the process or its token cannot be read. <paramref name="tokenDenied"/> says the process opened but
    /// its token was refused.
    /// </summary>
    internal static bool? TryRead(uint processId, out bool tokenDenied)
    {
        using var process = ProcessHandle.Open(processId);
        Span<byte> elevated = stackalloc byte[sizeof(int)];
        var read = process.ReadToken(TOKEN_INFORMATION_CLASS.TokenElevation, elevated);
        tokenDenied = read == TokenRead.Denied;
        return read == TokenRead.Read ? BitConverter.ToInt32(elevated) != 0 : null;
    }
}
