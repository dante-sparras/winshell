using Winshell.Native;

namespace Winshell.Input;

/// <summary>The <see cref="IHotkeyBackend"/> Windows provides, over <see cref="HotkeyWindow"/> and <see cref="KeyboardHook"/>.</summary>
internal sealed class HotkeyBackend : IHotkeyBackend
{
    private readonly IEventLoop _loop;
    private readonly HotkeyWindow _window;
    private readonly KeyboardHook _hook;
    private Func<int, bool, HotkeyModifiers, bool>? _onKey;
    private Action? _onLost;
    private bool _disposed;

    public HotkeyBackend(IEventLoop loop)
    {
        _loop = loop;
        _window = new HotkeyWindow(OnHotkey);
        _hook = new KeyboardHook(
            (virtualKey, up, modifiers) =>
            {
                var onKey = Volatile.Read(ref _onKey);
                return onKey is not null && onKey(virtualKey, up, modifiers);
            },
            () => Volatile.Read(ref _onLost)?.Invoke());
    }

    public event Action<OsChord>? ChordPressed;

    public bool IsHookInstalled => _hook.IsAttached;

    public HotkeyClaim Register(HotkeyChord chord)
    {
        return _window.Register(chord);
    }

    public void Unregister(OsChord chord)
    {
        _window.Unregister(chord);
    }

    public bool InstallHook(Func<int, bool, HotkeyModifiers, bool> onKey, Action onLost)
    {
        Volatile.Write(ref _onKey, onKey);
        Volatile.Write(ref _onLost, onLost);
        _hook.Attach();
        return _hook.IsAttached;
    }

    public void RemoveHook()
    {
        _hook.Detach();
        Volatile.Write(ref _onKey, null);
        Volatile.Write(ref _onLost, null);
    }

    public bool IsKeyDown(int virtualKey)
    {
        return Win32.GetAsyncKeyState(virtualKey) < 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        RemoveHook();
        _window.Dispose();
    }

    private void OnHotkey(int id)
    {
        if (_disposed || !_window.TryGetChord(id, out var chord))
            return;
        _loop.Post(() => ChordPressed?.Invoke(chord));
    }
}
