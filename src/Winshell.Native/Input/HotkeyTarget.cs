using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// A message-only window that holds <c>RegisterHotKey</c> ids and receives <c>WM_HOTKEY</c> on the thread that made
/// it. Its handle stays the same after disposal, so a late registration fails rather than registering the chord for
/// the calling thread, as a handle of 0 would.
/// </summary>
internal sealed class HotkeyTarget : IDisposable
{
    private const string ClassName = "WinshellHotkey";

    private readonly NativeWindow _window;

    private HotkeyTarget(NativeWindow window)
    {
        _window = window;
        Handle = window.Handle;
    }

    internal nint Handle { get; }

    /// <summary>Makes the window, or returns null when Windows would not.</summary>
    /// <param name="onHotkey">Runs on the window's thread with the id Windows saw pressed.</param>
    /// <param name="failed">Gets each exception <paramref name="onHotkey"/> throws.</param>
    internal static HotkeyTarget? Create(Action<int> onHotkey, Action<Exception> failed)
    {
        var window = NativeWindow.TryCreate(ClassName, 0, 0, message =>
        {
            if (message.Id != Win32.WM_HOTKEY)
                return null;
            onHotkey((int)message.WParam);
            return null;
        }, failed, true);
        return window is null ? null : new HotkeyTarget(window);
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
        _window.Release(true);
    }
}
