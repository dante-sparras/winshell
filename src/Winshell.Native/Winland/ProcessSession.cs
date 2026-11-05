using Windows.Win32;

namespace Winshell.Native;

/// <summary>The Remote Desktop Services session a process runs in.</summary>
internal static class ProcessSession
{
    /// <summary>The session id, or null when Windows could not say.</summary>
    internal static uint? Of(uint processId) => PInvoke.ProcessIdToSessionId(processId, out var session) ? session : null;
}
