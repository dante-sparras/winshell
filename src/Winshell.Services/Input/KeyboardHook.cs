using Winshell.Native;

namespace Winshell.Input;

/// <summary>
/// One listener on the process keyboard hook. While attached, every key goes to <paramref name="onKey"/>, which
/// returns true to swallow it. The process has one hook, so a second listener does not attach while another holds it.
/// </summary>
/// <param name="onKey">Runs on the hook thread with the key, whether it came up, and the modifiers Windows sees held.</param>
/// <param name="onLost">Runs on the hook thread when that thread ends without <see cref="Detach"/>.</param>
internal sealed class KeyboardHook(Func<int, bool, HotkeyModifiers, bool> onKey, Action onLost)
{
    private readonly object _gate = new();
    private IDisposable? _subscription;

    /// <summary>Whether this listener holds the process hook.</summary>
    public bool IsAttached
    {
        get
        {
            lock (_gate)
            {
                return _subscription is not null;
            }
        }
    }

    public void Attach()
    {
        lock (_gate)
        {
            if (_subscription is not null)
                return;
        }

        var subscription = LowLevelKeyboard.Start(OnKey, e => Log.Default.Warn("hotkey", e.Message), HookLost);
        lock (_gate)
        {
            if (_subscription is not null || subscription is null)
            {
                subscription?.Dispose();
                return;
            }

            _subscription = subscription;
        }
    }

    public void Detach()
    {
        IDisposable? subscription;
        lock (_gate)
        {
            subscription = _subscription;
            _subscription = null;
        }

        subscription?.Dispose();
    }

    private void HookLost()
    {
        lock (_gate)
        {
            _subscription = null;
        }

        onLost();
    }

    // Read the modifiers before the handler takes its lock: GetAsyncKeyState does not need it, and the lock stays short.
    private bool Handle(HookedKey key)
    {
        return onKey(key.VirtualKey, key.Up, ReadModifiers());
    }

    private static HotkeyModifiers ReadModifiers()
    {
        HotkeyModifiers mods = 0;
        if (Down(0x11) || Down(0xA2) || Down(0xA3))
            mods |= HotkeyModifiers.Ctrl;
        if (Down(0x12) || Down(0xA4) || Down(0xA5))
            mods |= HotkeyModifiers.Alt;
        if (Down(0x10) || Down(0xA0) || Down(0xA1))
            mods |= HotkeyModifiers.Shift;
        if (Down(VirtualKeys.LeftWin) || Down(VirtualKeys.RightWin))
            mods |= HotkeyModifiers.Win;
        return mods;
    }

    private static bool Down(int virtualKey)
    {
        return Win32.GetAsyncKeyState(virtualKey) < 0;
    }

    private bool OnKey(HookedKey key)
    {
        return !KeyMask.IsOurs(key.ExtraInfo) && Handle(key);
    }
}
