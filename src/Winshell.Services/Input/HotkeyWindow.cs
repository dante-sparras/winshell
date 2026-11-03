using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.Input;

/// <summary>
/// The message-only window that holds <c>RegisterHotKey</c> ids and receives <c>WM_HOTKEY</c>. It exists only while a
/// chord is registered.
/// </summary>
/// <param name="onHotkey">Runs on the window's thread with the id of the chord Windows saw pressed.</param>
internal sealed unsafe class HotkeyWindow(Action<int> onHotkey) : IDisposable
{
    private const int ErrorAlreadyRegistered = 1409;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint ModNoRepeat = 0x4000;
    private const string ClassName = "WinshellHotkey";

    private readonly object _lock = new();
    private readonly Dictionary<OsChord, int> _ids = [];
    private readonly Dictionary<int, OsChord> _chords = [];
    private GCHandle _self;
    private nint _hwnd;
    private int _nextId = 1;
    private bool _classRegistered;
    private bool _disposed;

    public nint Handle
    {
        get { lock (_lock) return _hwnd; }
    }

    public int Count
    {
        get { lock (_lock) return _ids.Count; }
    }

    public bool TryGetId(OsChord chord, out int id)
    {
        lock (_lock)
            return _ids.TryGetValue(chord, out id);
    }

    public bool TryGetChord(int id, out OsChord chord)
    {
        lock (_lock)
            return _chords.TryGetValue(id, out chord);
    }

    /// <summary>Registers <paramref name="chord"/>, or says why Windows would not. A chord already registered here is active.</summary>
    public HotkeyClaim Register(HotkeyChord chord)
    {
        if (!EnsureWindow())
            return HotkeyClaim.Invalid($"{chord.Text} could not be registered (0).");
        int id;
        nint hwnd;
        lock (_lock)
        {
            if (_ids.ContainsKey(chord.OsKey))
                return HotkeyClaim.Active;
            if (_nextId > 0xBFFF)
                return HotkeyClaim.Invalid($"{chord.Text} could not be registered (no id).");
            id = _nextId;
            hwnd = _hwnd;
        }
        if (!Win32.RegisterHotKey(hwnd, id, chord.Win32Modifiers | ModNoRepeat, (uint)chord.VirtualKey))
        {
            var code = Marshal.GetLastPInvokeError();
            return code == ErrorAlreadyRegistered
                ? HotkeyClaim.Taken($"{chord.Text} is already in use by Windows or another app.")
                : HotkeyClaim.Invalid($"{chord.Text} could not be registered ({code}).");
        }
        lock (_lock)
        {
            if (_disposed || _hwnd != hwnd)
            {
                Win32.UnregisterHotKey(hwnd, id);
                return HotkeyClaim.Invalid("Hotkeys are shut down.");
            }
            if (_ids.ContainsKey(chord.OsKey))
            {
                Win32.UnregisterHotKey(hwnd, id);
                return HotkeyClaim.Active;
            }
            _ids[chord.OsKey] = id;
            _chords[id] = chord.OsKey;
            _nextId = id + 1;
            return HotkeyClaim.Active;
        }
    }

    /// <summary>
    /// Unregisters each chord <paramref name="inUse"/> no longer wants. With none left, the window goes.
    /// <paramref name="inUse"/> runs under this window's lock: it may take the book's lock, and must not call back into
    /// this window, so the order is always this lock, then the book's.
    /// </summary>
    public void UnregisterUnused(Func<OsChord, bool> inUse)
    {
        List<int> ids = [];
        nint hwnd;
        lock (_lock)
        {
            foreach (var (chord, id) in _ids)
            {
                if (!inUse(chord))
                    ids.Add(id);
            }
            foreach (var id in ids)
            {
                if (_chords.Remove(id, out var chord))
                    _ids.Remove(chord);
            }
            hwnd = _hwnd;
        }
        if (hwnd != 0)
        {
            foreach (var id in ids)
                Win32.UnregisterHotKey(hwnd, id);
        }
        DestroyIfIdle();
    }

    public void Dispose()
    {
        int[] ids;
        nint hwnd;
        GCHandle self;
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            ids = [.. _ids.Values];
            _ids.Clear();
            _chords.Clear();
            hwnd = _hwnd;
            _hwnd = 0;
            self = _self;
            _self = default;
        }
        if (hwnd != 0)
        {
            foreach (var id in ids)
                Win32.UnregisterHotKey(hwnd, id);
            Win32.SetWindowLongPtr(hwnd, Win32.GWLP_USERDATA, 0);
            Win32.DestroyWindow(hwnd);
        }
        if (self.IsAllocated)
            self.Free();
    }

    private void DestroyIfIdle()
    {
        nint hwnd;
        GCHandle self;
        lock (_lock)
        {
            if (_ids.Count > 0 || _hwnd == 0)
                return;
            hwnd = _hwnd;
            _hwnd = 0;
            self = _self;
            _self = default;
        }
        Win32.SetWindowLongPtr(hwnd, Win32.GWLP_USERDATA, 0);
        Win32.DestroyWindow(hwnd);
        if (self.IsAllocated)
            self.Free();
    }

    private bool EnsureWindow()
    {
        lock (_lock)
        {
            if (_hwnd != 0)
                return true;
            if (_disposed)
                return false;
        }
        if (!_classRegistered && !RegisterClass())
            return false;
        _classRegistered = true;
        var hwnd = Win32.CreateWindowExW(0, ClassName, "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, Win32.GetModuleHandleW(null), 0);
        if (hwnd == 0)
            return false;
        var self = GCHandle.Alloc(this);
        Win32.SetWindowLongPtr(hwnd, Win32.GWLP_USERDATA, GCHandle.ToIntPtr(self));
        lock (_lock)
        {
            if (_disposed || _hwnd != 0)
            {
                Win32.SetWindowLongPtr(hwnd, Win32.GWLP_USERDATA, 0);
                Win32.DestroyWindow(hwnd);
                self.Free();
                return !_disposed && _hwnd != 0;
            }
            _hwnd = hwnd;
            _self = self;
            return true;
        }
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
                lpszClassName = name,
            };
            if (Win32.RegisterClassExW(wc) != 0)
                return true;
            return Marshal.GetLastPInvokeError() == ErrorClassAlreadyExists;
        }
    }

    private void OnHotkey(int id) => onHotkey(id);

    [UnmanagedCallersOnly]
    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == Win32.WM_HOTKEY)
        {
            NativeCallback.Guard(Log.Default, "hotkey", () =>
            {
                var data = Win32.GetWindowLongPtr(hwnd, Win32.GWLP_USERDATA);
                if (data != 0 && GCHandle.FromIntPtr(data).Target is HotkeyWindow window)
                    window.OnHotkey((int)wParam);
            });
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
