using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// A message-only window that holds <c>RegisterHotKey</c> ids and receives <c>WM_HOTKEY</c> on the thread that made
/// it. Its handle stays the same after disposal, so a late registration fails rather than registering the chord for
/// the calling thread, as a handle of 0 would.
/// </summary>
internal sealed unsafe class HotkeyTarget : IDisposable
{
    private const int ErrorClassAlreadyExists = 1410;
    private const string ClassName = "WinshellHotkey";

    private static bool ClassRegistered;

    private readonly Action<int> _onHotkey;
    private GCHandle _self;
    private int _disposed;

    private HotkeyTarget(nint handle, Action<int> onHotkey)
    {
        Handle = handle;
        _onHotkey = onHotkey;
    }

    internal nint Handle { get; }

    /// <summary>Makes the window, or returns null when Windows would not.</summary>
    /// <param name="onHotkey">
    /// Runs on the window's thread with the id Windows saw pressed. Its exception does not reach Windows, so it logs
    /// its own.
    /// </param>
    internal static HotkeyTarget? Create(Action<int> onHotkey)
    {
        if (!ClassRegistered && !RegisterClass())
            return null;
        ClassRegistered = true;
        var hwnd = Win32.CreateWindowExW(0, ClassName, "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0,
            Win32.GetModuleHandleW(null), 0);
        if (hwnd == 0)
            return null;
        var target = new HotkeyTarget(hwnd, onHotkey);
        target._self = GCHandle.Alloc(target);
        Win32.SetWindowLongPtr(hwnd, Win32.GWLP_USERDATA, GCHandle.ToIntPtr(target._self));
        return target;
    }

    /// <summary>Registers a chord under <paramref name="id"/>, or says with the Win32 error why Windows would not.</summary>
    internal bool Register(int id, uint modifiers, uint virtualKey, out int error)
    {
        var registered = Win32.RegisterHotKey(Handle, id, modifiers, virtualKey);
        error = registered ? 0 : Marshal.GetLastPInvokeError();
        return registered;
    }

    internal void Unregister(int id)
    {
        Win32.UnregisterHotKey(Handle, id);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Win32.SetWindowLongPtr(Handle, Win32.GWLP_USERDATA, 0);
        Win32.DestroyWindow(Handle);
        _self.Free();
    }

    private static bool RegisterClass()
    {
        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &Proc,
                hInstance = Win32.GetModuleHandleW(null),
                lpszClassName = name
            };
            if (Win32.RegisterClassExW(wc) != 0)
                return true;
            return Marshal.GetLastPInvokeError() == ErrorClassAlreadyExists;
        }
    }

    [UnmanagedCallersOnly]
    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == Win32.WM_HOTKEY)
            try
            {
                var data = Win32.GetWindowLongPtr(hwnd, Win32.GWLP_USERDATA);
                if (data != 0 && GCHandle.FromIntPtr(data).Target is HotkeyTarget target)
                    target._onHotkey((int)wParam);
            }
#pragma warning disable CA1031
            // Windows called in; nothing above this frame can catch it.
            catch (Exception)
#pragma warning restore CA1031
            {
            }

        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
