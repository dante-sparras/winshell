using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>Answers a window message, or returns null to leave it to Windows.</summary>
internal delegate nint? WindowMessageHandler(uint msg, nint wParam, nint lParam);

/// <summary>
/// A top-level window of a class this process registers, whose messages go to a handler on the thread that made it.
/// The class carries the logo embedded in the program. A handler's exception does not reach Windows: the message
/// goes to Windows' default handling, so a handler reports its own.
/// </summary>
internal sealed unsafe class NativeWindow
{
    private static readonly ConcurrentDictionary<nint, NativeWindow> Live = new();
    private static readonly ConcurrentDictionary<string, byte> Registered = new();

    private readonly WindowMessageHandler _handler;

    private NativeWindow(nint handle, WindowMessageHandler handler)
    {
        Handle = handle;
        _handler = handler;
    }

    /// <summary>The window, or 0 once released.</summary>
    internal nint Handle { get; private set; }

    /// <summary>Makes a window of <paramref name="className"/>, registered once in the process. Throws when Windows would not.</summary>
    internal static NativeWindow Create(string className, uint style, uint exStyle, WindowMessageHandler handler)
    {
        if (Registered.TryAdd(className, 0))
            Register(className);
        var hwnd = Win32.CreateWindowExW(exStyle, className, "", style, 0, 0, 1, 1, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        var window = new NativeWindow(hwnd, handler);
        Live[hwnd] = window;
        return window;
    }

    /// <summary>
    /// Stops the handler getting messages, then destroys the window when <paramref name="destroy"/> is true. False
    /// leaves it to whoever is destroying it already, as Windows does a window whose owner closes.
    /// </summary>
    internal void Release(bool destroy)
    {
        var hwnd = Handle;
        if (hwnd == 0)
            return;
        Handle = 0;
        Live.TryRemove(hwnd, out _);
        if (destroy)
            Win32.DestroyWindow(hwnd);
    }

    /// <summary>The rectangle a <c>WM_DPICHANGED</c> message suggests, in physical pixels.</summary>
    internal static RECT SuggestedRect(nint lParam) => *(RECT*)lParam;

    /// <summary>The track sizes a <c>WM_GETMINMAXINFO</c> message carries, which its handler changes in place.</summary>
    internal static ref MINMAXINFO MinMaxInfo(nint lParam) => ref *(MINMAXINFO*)lParam;

    private static void Register(string className)
    {
        fixed (char* name = className)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = Win32.CS_DBLCLKS,
                lpfnWndProc = &WndProc,
                hInstance = Win32.GetModuleHandleW(null),
                hIcon = AppIcon(Win32.GetSystemMetrics(Win32.SM_CXICON), Win32.GetSystemMetrics(Win32.SM_CYICON)),
                hIconSm = AppIcon(Win32.GetSystemMetrics(Win32.SM_CXSMICON), Win32.GetSystemMetrics(Win32.SM_CYSMICON)),
                hCursor = Win32.LoadCursorW(0, Win32.IDC_ARROW),
                lpszClassName = name,
            };
            Win32.RegisterClassExW(wc);
        }
    }

    /// <summary>
    /// The logo embedded in the program. The C# compiler stores <c>ApplicationIcon</c> as icon group 32512, so the
    /// icon shows on a window of this class that has a caption.
    /// </summary>
    private static nint AppIcon(int width, int height) =>
        Win32.LoadImageW(Win32.GetModuleHandleW(null), Win32.IDI_APPLICATION, Win32.IMAGE_ICON, width, height, 0);

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (Live.TryGetValue(hwnd, out var window))
        {
            try
            {
                if (window._handler(msg, wParam, lParam) is { } result)
                    return result;
            }
#pragma warning disable CA1031 // Windows called in; nothing above this frame can catch it.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
