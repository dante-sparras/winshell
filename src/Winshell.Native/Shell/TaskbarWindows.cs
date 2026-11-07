using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winshell.Native;

/// <summary>Explorer's taskbar windows as Windows names them, and a way to hear Explorer make new ones.</summary>
internal static unsafe class TaskbarWindows
{
    /// <summary>The class of the taskbar on the primary monitor. A shell's tray host makes a window of it too.</summary>
    internal const string PrimaryClass = "Shell_TrayWnd";

    /// <summary>The class of the taskbar on every other monitor.</summary>
    internal const string SecondaryClass = "Shell_SecondaryTrayWnd";

    /// <summary>
    /// The message Explorer broadcasts to top-level windows once it has made its taskbar: when it starts, after a crash
    /// or an update. Apps then add their notification area icons again, and app bars register again.
    /// </summary>
    internal static readonly uint Created = Win32.RegisterWindowMessageW("TaskbarCreated");

    /// <summary>
    /// Calls <paramref name="changed"/> on this thread when Explorer may have made a taskbar window: on
    /// <see cref="Created"/>, and when the displays change, after which Explorer makes one for a new monitor. Both are
    /// broadcasts, which reach only top-level windows, so this makes a hidden one. Dispose it to stop. Null when
    /// Windows would not make the window.
    /// </summary>
    /// <param name="failed">Gets each exception <paramref name="changed"/> throws.</param>
    internal static IDisposable? Watch(Action changed, Action<Exception> failed)
    {
        var window = NativeWindow.TryCreate("WinshellTaskbarWatch", (uint)WINDOW_STYLE.WS_POPUP,
            (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
            message =>
            {
                if (message.Id != Created && message.Id != Win32.WM_DISPLAYCHANGE)
                    return null;
                changed();
                return 0;
            }, failed, false);
        if (window is null)
            return null;
        // Explorer lets this through from any integrity level, so an elevated shell still hears Explorer start.
        PInvoke.ChangeWindowMessageFilterEx((HWND)window.Handle, Created, WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW,
            null);
        return new Watcher(window);
    }

    private sealed class Watcher(NativeWindow window) : IDisposable
    {
        public void Dispose()
        {
            window.Release(true);
        }
    }
}
