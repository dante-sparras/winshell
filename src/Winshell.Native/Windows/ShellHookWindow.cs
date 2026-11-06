using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>
/// A message-only window that gets the shell hook messages Explorer's taskbar gets, for the process. Windows calls
/// the handler on the thread that started it. A handler's exception does not reach Windows, so a handler logs its own.
/// </summary>
internal static class ShellHookWindow
{
    /// <summary><c>HSHELL_FLASH</c>: a window flashed to ask for attention.</summary>
    internal const nint Flash = (nint)(PInvoke.HSHELL_REDRAW | PInvoke.HSHELL_HIGHBIT);

    private const string ClassName = "WinshellShellHook";

    private static NativeWindow? _window;
    private static uint _message;
    private static Action<nint, nint>? _received;

    /// <summary>
    /// Creates the window and calls <paramref name="received"/> with each message's code and window. Call it once.
    /// False when Windows refused the window or the registration. A refused window is destroyed.
    /// </summary>
    internal static bool Start(Action<nint, nint> received)
    {
        _received = received;
        _message = Win32.RegisterWindowMessageW("SHELLHOOK");
        _window = NativeWindow.TryCreate(ClassName, 0, 0, OnMessage, true);
        if (_window is not null && _message != 0 && PInvoke.RegisterShellHookWindow((HWND)_window.Handle))
            return true;
        _window?.Release(true);
        _window = null;
        return false;
    }

    private static nint? OnMessage(WindowMessage message)
    {
        if (message.Id != _message || _message == 0)
            return null;
        _received?.Invoke(message.WParam, message.LParam);
        return 0;
    }
}
