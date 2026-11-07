using Winshell.Native;

namespace Winshell.Input;

/// <summary>
/// The <see cref="IHotkeyBackend"/> Windows provides. Registered chords belong to a message-only window made on the
/// loop's thread, which receives <c>WM_HOTKEY</c> there and exists only while a chord is registered. The hook is
/// <see cref="LowLevelKeyboard"/>.
/// </summary>
/// <param name="loop">The loop the chords are registered on.</param>
/// <param name="failed">Gets each exception a Windows callback throws.</param>
internal sealed class HotkeyBackend(IEventLoop loop, Action<Exception> failed) : IHotkeyBackend
{
    private const int ErrorAlreadyRegistered = 1409;
    private const uint ModNoRepeat = 0x4000;

    // RegisterHotKey takes application ids from 0x0000 to 0xBFFF.
    private const int LastId = 0xBFFF;

    private readonly Dictionary<OsChord, int> _ids = [];
    private readonly Dictionary<int, OsChord> _chords = [];
    private HotkeyTarget? _target;
    private int _nextId = 1;
    private IDisposable? _hook;

    public event Action<OsChord>? ChordPressed;

    public HotkeyClaim Register(HotkeyChord chord)
    {
        if (_nextId > LastId)
            return HotkeyClaim.Invalid($"{chord.Text} could not be registered (no id).");
        _target ??= HotkeyTarget.Create(OnHotkey, failed);
        if (_target is null)
            return HotkeyClaim.Invalid($"{chord.Text} could not be registered (0).");
        var id = _nextId;
        if (!_target.Register(id, (uint)chord.Modifiers | ModNoRepeat, (uint)chord.VirtualKey, out var code))
        {
            DestroyIfIdle();
            return code == ErrorAlreadyRegistered
                ? HotkeyClaim.Taken($"{chord.Text} is already in use by Windows or another app.")
                : HotkeyClaim.Invalid($"{chord.Text} could not be registered ({code}).");
        }

        _nextId++;
        _ids[chord.OsKey] = id;
        _chords[id] = chord.OsKey;
        return HotkeyClaim.Active;
    }

    public void Unregister(OsChord chord)
    {
        if (!_ids.Remove(chord, out var id))
            return;
        _chords.Remove(id);
        _target?.Unregister(id);
        DestroyIfIdle();
    }

    public bool InstallHook(Func<int, bool, bool> onKey, Action onLost)
    {
        if (_hook is not null)
            return true;
        IDisposable? hook = null;
        hook = LowLevelKeyboard.Start(key => onKey(key.VirtualKey, key.Up), failed, () => loop.Post(() =>
        {
            if (hook is null || !ReferenceEquals(_hook, hook))
                return;
            _hook = null;
            onLost();
        }));
        _hook = hook;
        return hook is not null;
    }

    public void RemoveHook()
    {
        var hook = _hook;
        _hook = null;
        hook?.Dispose();
    }

    public bool IsKeyDown(int virtualKey)
    {
        return Win32.GetAsyncKeyState(virtualKey) < 0;
    }

    public void SendMask()
    {
        KeyInput.Tap();
    }

    public void Dispose()
    {
        RemoveHook();
        foreach (var chord in _ids.Keys.ToList())
            Unregister(chord);
    }

    private void DestroyIfIdle()
    {
        if (_ids.Count > 0)
            return;
        _target?.Dispose();
        _target = null;
    }

    private void OnHotkey(int id)
    {
        if (_chords.TryGetValue(id, out var chord))
            loop.Post(() => ChordPressed?.Invoke(chord));
    }
}
