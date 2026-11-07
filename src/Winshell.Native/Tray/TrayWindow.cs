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
    internal ReadOnlySpan<byte> Payload =>
        _data->lpData == null ? default : new ReadOnlySpan<byte>(_data->lpData, (int)_data->cbData);

    /// <summary>
    /// Sends the message on to <paramref name="window"/> as it arrived, and returns that window's answer; 0 when it
    /// hangs or does not answer in time.
    /// </summary>
    internal nint Forward(nint window, uint timeoutMilliseconds)
    {
        Win32.SendMessageTimeoutW(window, PInvoke.WM_COPYDATA, _sender, (nint)_data, Win32.SMTO_ABORTIFHUNG,
            timeoutMilliseconds, out var result);
        return result;
    }
}

/// <summary>Answers a <c>WM_COPYDATA</c> message.</summary>
internal delegate nint CopyDataHandler(CopyData data);

/// <summary>
/// A window of a taskbar class that apps send their notification area icons to. Layered and transparent, so it can
/// report Explorer's taskbar rectangle without taking its clicks; topmost and never activated. Its messages arrive on
/// the thread that made it. A message whose handler throws goes to Windows' default handling.
/// </summary>
internal sealed unsafe class TrayWindow : IDisposable
{
    private readonly NativeWindow _window;

    private TrayWindow(NativeWindow window)
    {
        _window = window;
    }

    /// <summary>The window, or 0 once disposed.</summary>
    internal nint Handle => _window.Handle;

    /// <summary>Makes the window, or returns null when Windows would not.</summary>
    /// <param name="className">The window class, registered once in the process.</param>
    /// <param name="copyData">Answers each <c>WM_COPYDATA</c> message.</param>
    /// <param name="message">Gets every other message, and returns true when it handled one, which is then answered with 0.</param>
    /// <param name="failed">Gets each exception <paramref name="copyData"/> or <paramref name="message"/> throws.</param>
    internal static TrayWindow? Create(string className, CopyDataHandler copyData, Func<uint, nint, nint, bool> message,
        Action<Exception> failed)
    {
        var window = NativeWindow.TryCreate(className, (uint)WINDOW_STYLE.WS_POPUP,
            (uint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_NOACTIVATE |
                   WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT), OnMessage(copyData, message),
            failed, false);
        if (window is null)
            return null;
        Win32.SetLayeredWindowAttributes(window.Handle, 0, 0, Win32.LWA_ALPHA);
        // Explorer's taskbar lets these through from any integrity level, so an elevated shell still hears Explorer
        // start, and an app at a lower level can still add its icon.
        foreach (var allowed in new[] { TaskbarWindows.Created, PInvoke.WM_COPYDATA })
            PInvoke.ChangeWindowMessageFilterEx((HWND)window.Handle, allowed, WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW,
                null);
        return new TrayWindow(window);
    }

    public void Dispose()
    {
        _window.Release(true);
    }

    private static WindowMessageHandler OnMessage(CopyDataHandler copyData, Func<uint, nint, nint, bool> message)
    {
        return received =>
        {
            if (received.Id == PInvoke.WM_COPYDATA)
                return received.LParam == 0
                    ? 0
                    : copyData(new CopyData(received.WParam, (COPYDATASTRUCT*)received.LParam));
            return message(received.Id, received.WParam, received.LParam) ? 0 : null;
        };
    }
}
