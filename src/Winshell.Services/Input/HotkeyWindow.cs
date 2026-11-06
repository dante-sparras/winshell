using Winshell.Native;

namespace Winshell.Input;

/// <summary>
/// The message-only window that holds <c>RegisterHotKey</c> ids and receives <c>WM_HOTKEY</c>. It exists only while a
/// chord is registered.
/// </summary>
/// <param name="onHotkey">Runs on the window's thread with the id of the chord Windows saw pressed.</param>
internal sealed class HotkeyWindow(Action<int> onHotkey) : IDisposable
{
    private const int ErrorAlreadyRegistered = 1409;
    private const uint ModNoRepeat = 0x4000;

    private readonly object _lock = new();
    private readonly Dictionary<OsChord, int> _ids = [];
    private readonly Dictionary<int, OsChord> _chords = [];
    private HotkeyTarget? _target;
    private int _nextId = 1;
    private bool _disposed;

    public nint Handle
    {
        get
        {
            lock (_lock)
            {
                return _target?.Handle ?? 0;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _ids.Count;
            }
        }
    }

    public bool TryGetId(OsChord chord, out int id)
    {
        lock (_lock)
        {
            return _ids.TryGetValue(chord, out id);
        }
    }

    public bool TryGetChord(int id, out OsChord chord)
    {
        lock (_lock)
        {
            return _chords.TryGetValue(id, out chord);
        }
    }

    /// <summary>Registers <paramref name="chord"/>, or says why Windows would not. A chord already registered here is active.</summary>
    public HotkeyClaim Register(HotkeyChord chord)
    {
        if (!EnsureWindow())
            return HotkeyClaim.Invalid($"{chord.Text} could not be registered (0).");
        int id;
        HotkeyTarget target;
        lock (_lock)
        {
            if (_ids.ContainsKey(chord.OsKey))
                return HotkeyClaim.Active;
            if (_nextId > 0xBFFF)
                return HotkeyClaim.Invalid($"{chord.Text} could not be registered (no id).");
            id = _nextId;
            target = _target!;
        }

        if (!target.Register(id, chord.Win32Modifiers | ModNoRepeat, (uint)chord.VirtualKey, out var code))
        {
            // A refused chord must not leave the window up when it is the only reason the window exists.
            DestroyIfIdle();
            return code == ErrorAlreadyRegistered
                ? HotkeyClaim.Taken($"{chord.Text} is already in use by Windows or another app.")
                : HotkeyClaim.Invalid($"{chord.Text} could not be registered ({code}).");
        }

        lock (_lock)
        {
            if (_disposed || _target != target)
            {
                target.Unregister(id);
                return HotkeyClaim.Invalid("Hotkeys are shut down.");
            }

            if (_ids.ContainsKey(chord.OsKey))
            {
                target.Unregister(id);
                return HotkeyClaim.Active;
            }

            _ids[chord.OsKey] = id;
            _chords[id] = chord.OsKey;
            _nextId = id + 1;
            return HotkeyClaim.Active;
        }
    }

    /// <summary>Gives one chord back. With none left, the window goes.</summary>
    public void Unregister(OsChord chord)
    {
        int id;
        HotkeyTarget? target;
        lock (_lock)
        {
            if (!_ids.Remove(chord, out id))
                return;
            _chords.Remove(id);
            target = _target;
        }

        target?.Unregister(id);
        DestroyIfIdle();
    }

    public void Dispose()
    {
        int[] ids;
        HotkeyTarget? target;
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            ids = [.. _ids.Values];
            _ids.Clear();
            _chords.Clear();
            target = _target;
            _target = null;
        }

        if (target is not null)
        {
            foreach (var id in ids)
                target.Unregister(id);
            target.Dispose();
        }
    }

    private void DestroyIfIdle()
    {
        HotkeyTarget target;
        lock (_lock)
        {
            if (_ids.Count > 0 || _target is null)
                return;
            target = _target;
            _target = null;
        }

        target.Dispose();
    }

    private bool EnsureWindow()
    {
        lock (_lock)
        {
            if (_target is not null)
                return true;
            if (_disposed)
                return false;
        }

        var target = HotkeyTarget.Create(onHotkey, e => Log.Default.Warn("hotkey", e.Message));
        if (target is null)
            return false;
        lock (_lock)
        {
            if (_disposed || _target is not null)
            {
                target.Dispose();
                return !_disposed && _target is not null;
            }

            _target = target;
            return true;
        }
    }
}
