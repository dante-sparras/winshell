using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace Winshell.Native;

/// <summary>A window message as it arrived, and what its pointer carries, readable only while it is handled.</summary>
internal readonly unsafe ref struct WindowMessage
{
    internal WindowMessage(uint id, nint wParam, nint lParam)
    {
        Id = id;
        WParam = wParam;
        LParam = lParam;
    }

    internal uint Id { get; }

    internal nint WParam { get; }

    internal nint LParam { get; }

    /// <summary>The rectangle a <c>WM_DPICHANGED</c> message suggests, in physical pixels.</summary>
    internal RECT SuggestedRect
    {
        get
        {
            Expect(Win32.WM_DPICHANGED);
            return *(RECT*)LParam;
        }
    }

    /// <summary>The track sizes a <c>WM_GETMINMAXINFO</c> message carries, which its handler changes in place.</summary>
    internal ref MINMAXINFO MinMaxInfo
    {
        get
        {
            Expect(Win32.WM_GETMINMAXINFO);
            return ref *(MINMAXINFO*)LParam;
        }
    }

    private void Expect(uint id)
    {
        if (Id != id || LParam == 0)
            throw new InvalidOperationException($"Message 0x{Id:X4} does not carry what message 0x{id:X4} does.");
    }
}

/// <summary>Answers a window message, or returns null to leave it to Windows.</summary>
internal delegate nint? WindowMessageHandler(WindowMessage message);

/// <summary>
/// The one window procedure in Native. A class is registered once, and each window's messages go to its handler on
/// the thread that made it. The class carries the logo embedded in the program. A handler's exception does not reach
/// Windows: the message goes to Windows' default handling, so a handler reports its own. <c>WM_NCDESTROY</c> drops
/// the handler, including a window nobody released.
/// </summary>
internal sealed unsafe class NativeWindow
{
    private const int ErrorClassAlreadyExists = 1410;

    private static readonly ConcurrentDictionary<nint, NativeWindow> Live = new();
    private static readonly ConcurrentDictionary<string, byte> Registered = new();

    private readonly WindowMessageHandler _handler;

    private NativeWindow(nint handle, WindowMessageHandler handler)
    {
        Handle = handle;
        _handler = handler;
    }

    /// <summary>The window, or 0 once released or destroyed.</summary>
    internal nint Handle { get; private set; }

    /// <summary>Makes a window of <paramref name="className"/>, registered once in the process. Throws when Windows would not.</summary>
    internal static NativeWindow Create(string className, uint style, uint exStyle, WindowMessageHandler handler)
    {
        return Make(className, style, exStyle, handler, 0, 1)
               ?? throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
    }

    /// <summary>
    /// Makes a window of <paramref name="className"/>, registered once in the process. Null when Windows would not.
    /// <paramref name="messageOnly"/> keeps it off the desktop: only messages sent to it arrive.
    /// </summary>
    internal static NativeWindow? TryCreate(string className, uint style, uint exStyle, WindowMessageHandler handler,
        bool messageOnly)
    {
        var parent = messageOnly ? Win32.HWND_MESSAGE : 0;
        return Make(className, style, exStyle, handler, parent, 0);
    }

    private static NativeWindow? Make(string className, uint style, uint exStyle, WindowMessageHandler handler,
        nint parent, int size)
    {
        if (Registered.TryAdd(className, 0))
            Register(className);
        var hwnd = Win32.CreateWindowExW(exStyle, className, "", style, 0, 0, size, size, parent, 0,
            Win32.GetModuleHandleW(null), 0);
        if (hwnd == 0)
            return null;
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
        Forget(hwnd);
        if (destroy)
            Win32.DestroyWindow(hwnd);
    }

    private void Forget(nint hwnd)
    {
        Live.TryRemove(new KeyValuePair<nint, NativeWindow>(hwnd, this));
        if (Handle == hwnd)
            Handle = 0;
    }

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
                lpszClassName = name
            };
            if (Win32.RegisterClassExW(wc) != 0 || Marshal.GetLastPInvokeError() == ErrorClassAlreadyExists)
                return;
        }

        Registered.TryRemove(className, out _);
    }

    /// <summary>
    /// The logo embedded in the program. The C# compiler stores <c>ApplicationIcon</c> as icon group 32512, so the
    /// icon shows on a window of this class that has a caption.
    /// </summary>
    private static nint AppIcon(int width, int height)
    {
        return Win32.LoadImageW(Win32.GetModuleHandleW(null), Win32.IDI_APPLICATION, Win32.IMAGE_ICON, width, height,
            0);
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (Live.TryGetValue(hwnd, out var window))
            try
            {
                if (window._handler(new WindowMessage(msg, wParam, lParam)) is { } result)
                    return result;
            }
#pragma warning disable CA1031
            // Windows called in; nothing above this frame can catch it.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
            finally
            {
                // The last message a window gets, so a window its handler did not release lets go of it here.
                if (msg == PInvoke.WM_NCDESTROY)
                    window.Forget(hwnd);
            }

        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
