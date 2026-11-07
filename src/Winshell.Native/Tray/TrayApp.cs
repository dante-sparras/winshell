using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>Calls on the app behind a notification area icon.</summary>
internal static class TrayApp
{
    /// <summary>Posts a message to the app's window without waiting for it to be handled.</summary>
    internal static bool Notify(nint window, uint message, nint wParam, nint lParam)
    {
        return PInvoke.SendNotifyMessage((HWND)window, message, (WPARAM)(nuint)wParam, lParam);
    }

    /// <summary>
    /// Asks every app to add its icons again, which it sends to the first <c>Shell_TrayWnd</c> window it finds: posts
    /// <c>TaskbarCreated</c> to every top-level window, and to every message-only window, which a broadcast does not
    /// reach and where some apps, such as Hex, keep their icons.
    /// </summary>
    internal static void AskForIcons()
    {
        Win32.PostMessageW((nint)HWND.HWND_BROADCAST, TaskbarWindows.Created, 0, 0);
        foreach (var window in WindowTree.MessageOnly())
            Win32.PostMessageW(window, TaskbarWindows.Created, 0, 0);
    }

    /// <summary>Lets the process take the foreground, if this process may pass that right on.</summary>
    internal static bool AllowForeground(uint processId)
    {
        return PInvoke.AllowSetForegroundWindow(processId);
    }
}
