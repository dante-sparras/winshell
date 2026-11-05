using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.DataExchange;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winshell.Native;

/// <summary>A <c>WM_COPYDATA</c> message as it arrived: what it carries, and the means to pass it on unchanged.</summary>
internal readonly unsafe ref struct CopyData
{
    private readonly nint _sender;
    private readonly COPYDATASTRUCT* _data;

    internal CopyData(nint sender, COPYDATASTRUCT* data)
    {
        _sender = sender;
        _data = data;
    }

    /// <summary>The number the sender put in <c>dwData</c>, which says what the payload is.</summary>
    internal nuint Kind => _data->dwData;

    /// <summary>The bytes the sender passed, which Windows copied into this process for the message.</summary>
    internal ReadOnlySpan<byte> Payload => _data->lpData == null ? default : new(_data->lpData, (int)_data->cbData);

    /// <summary>
    /// Sends the message on to <paramref name="window"/> as it arrived, and returns that window's answer; 0 when it
    /// hangs or does not answer in time.
    /// </summary>
    internal nint Forward(nint window, uint timeoutMilliseconds)
    {
        Win32.SendMessageTimeoutW(window, PInvoke.WM_COPYDATA, _sender, (nint)_data, Win32.SMTO_ABORTIFHUNG, timeoutMilliseconds, out var result);
        return result;
    }
}

/// <summary>Answers a <c>WM_COPYDATA</c> message.</summary>
internal delegate nint CopyDataHandler(CopyData data);

/// <summary>
/// A window of a taskbar class that apps send their notification area icons to. Layered and transparent, so it can
/// report Explorer's taskbar rectangle without taking its clicks; topmost and never activated. Its messages arrive on
/// the thread that made it. A handler's exception does not reach Windows: the message is answered with 0, so a
/// handler logs its own.
/// </summary>
internal sealed unsafe class TrayWindow : IDisposable
{
    private static readonly ConcurrentDictionary<nint, TrayWindow> Live = new();
    private static readonly ConcurrentDictionary<string, byte> Registered = new();

    private readonly CopyDataHandler _copyData;
    private readonly Func<uint, nint, nint, bool> _message;

    private TrayWindow(nint handle, CopyDataHandler copyData, Func<uint, nint, nint, bool> message)
    {
        Handle = handle;
        _copyData = copyData;
        _message = message;
    }

    /// <summary>The window, or 0 once disposed.</summary>
    internal nint Handle { get; private set; }

    /// <summary>Makes the window, or returns null when Windows would not.</summary>
    /// <param name="className">The window class, registered once in the process.</param>
    /// <param name="copyData">Answers each <c>WM_COPYDATA</c> message.</param>
    /// <param name="message">Gets every other message, and returns true when it handled one, which is then answered with 0.</param>
    internal static TrayWindow? Create(string className, CopyDataHandler copyData, Func<uint, nint, nint, bool> message)
    {
        if (Registered.TryAdd(className, 0))
        {
            fixed (char* name = className)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    hInstance = Win32.GetModuleHandleW(null),
                    lpszClassName = name,
                };
                Win32.RegisterClassExW(wc);
            }
        }
        var hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT,
            className, "", Win32.WS_POPUP, 0, 0, 0, 0, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (hwnd == 0)
            return null;
        var window = new TrayWindow(hwnd, copyData, message);
        Live[hwnd] = window;
        Win32.SetLayeredWindowAttributes(hwnd, 0, 0, Win32.LWA_ALPHA);
        return window;
    }

    /// <summary>Lets <paramref name="message"/> through from a process at any integrity level.</summary>
    internal void AllowFromAnyLevel(uint message) =>
        PInvoke.ChangeWindowMessageFilterEx((HWND)Handle, message, WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW, null);

    public void Dispose()
    {
        if (Handle == 0)
            return;
        Live.TryRemove(Handle, out _);
        Win32.DestroyWindow(Handle);
        Handle = 0;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (!Live.TryGetValue(hwnd, out var window))
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        try
        {
            if (msg == PInvoke.WM_COPYDATA)
                return lParam == 0 ? 0 : window._copyData(new CopyData(wParam, (COPYDATASTRUCT*)lParam));
            if (window._message(msg, wParam, lParam))
                return 0;
        }
#pragma warning disable CA1031 // Windows called in; nothing above this frame can catch it.
        catch (Exception)
#pragma warning restore CA1031
        {
            return 0;
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
