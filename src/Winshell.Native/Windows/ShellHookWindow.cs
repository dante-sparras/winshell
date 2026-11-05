using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>
/// A message-only window that gets the shell hook messages Explorer's taskbar gets, for the process. Windows calls
/// the handler on the thread that started it, inside a window procedure. A handler's exception does not reach Windows,
/// so a handler logs its own.
/// </summary>
internal static unsafe class ShellHookWindow
{
    /// <summary><c>HSHELL_FLASH</c>: a window flashed to ask for attention.</summary>
    internal const nint Flash = (nint)(PInvoke.HSHELL_REDRAW | PInvoke.HSHELL_HIGHBIT);

    private const string ClassName = "WinshellShellHook";

    private static uint _message;
    private static Action<nint, nint>? _received;

    /// <summary>
    /// Creates the window and calls <paramref name="received"/> with each message's code and window. Call it once.
    /// False when Windows refused the window or the registration.
    /// </summary>
    internal static bool Start(Action<nint, nint> received)
    {
        _received = received;
        _message = Win32.RegisterWindowMessageW("SHELLHOOK");
        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &Proc,
                hInstance = Win32.GetModuleHandleW(null),
                lpszClassName = name
            };
            Win32.RegisterClassExW(wc);
        }

        var hwnd = Win32.CreateWindowExW(0, ClassName, "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0,
            Win32.GetModuleHandleW(null), 0);
        return hwnd != 0 && _message != 0 && PInvoke.RegisterShellHookWindow((HWND)hwnd);
    }

    [UnmanagedCallersOnly]
    private static nint Proc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message != _message || _message == 0)
            return Win32.DefWindowProcW(hwnd, message, wParam, lParam);
        try
        {
            _received?.Invoke(wParam, lParam);
        }
#pragma warning disable CA1031 // Windows called in; nothing above this frame can catch it.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        return 0;
    }
}
