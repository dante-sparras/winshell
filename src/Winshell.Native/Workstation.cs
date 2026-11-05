using Windows.Win32;

namespace Winshell.Native;

internal static class Workstation
{
    /// <summary>Returns whether Windows accepted the asynchronous request to lock the session.</summary>
    internal static bool Lock() => PInvoke.LockWorkStation();
}
