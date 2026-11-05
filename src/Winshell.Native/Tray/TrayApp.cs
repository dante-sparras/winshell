using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>Calls on the app behind a notification area icon.</summary>
internal static class TrayApp
{
    /// <summary>Posts a message to the app's window without waiting for it to be handled.</summary>
    internal static bool Notify(nint window, uint message, nint wParam, nint lParam) =>
        PInvoke.SendNotifyMessage((HWND)window, message, (WPARAM)(nuint)wParam, lParam);

    /// <summary>Lets the process take the foreground, if this process may pass that right on.</summary>
    internal static bool AllowForeground(uint processId) => PInvoke.AllowSetForegroundWindow(processId);
}
