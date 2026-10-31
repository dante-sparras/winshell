using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Input;

/// <summary>
/// Registers hotkeys with <c>RegisterHotKey</c>, and installs one low-level keyboard hook while a Windows-key
/// hotkey is claimed or a key sequence is waiting for its next chord.
/// </summary>
internal sealed unsafe class WindowsHotkeyRegistrar : IHotkeyRegistrar, IDisposable
{
    private const int ErrorAlreadyRegistered = 1409;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint ModNoRepeat = 0x4000;
    private const uint LlkhfUp = 0x80;
    private const string ClassName = "WinshellHotkey";

    private static readonly object Gate = new();
    private static HookThread? Hook;
    private static WindowsHotkeyRegistrar? HookOwner;

    private readonly object _bookLock = new();
    private readonly IEventLoop _loop;
    private readonly HotkeyBook _book = new();
    private readonly Dictionary<OsChord, int> _ids = [];
    private readonly Dictionary<int, OsChord> _chords = [];
    private readonly Dictionary<IHotkeyListener, IDisposable> _timers = [];
    private readonly List<int> _pendingUnregister = [];
    private GCHandle _self;
    private nint _hwnd;
    private int _nextId = 1;
    private int _syncPosted;
    private bool _classRegistered;
    private bool _disposed;

    public WindowsHotkeyRegistrar(IEventLoop loop) => _loop = loop;

    /// <summary>When false, a sequence can arm without installing a hook. Tests use this so a hook is never left behind.</summary>
    internal bool InstallNativeHook { get; set; } = true;

    internal TimeSpan SequenceTimeout
    {
        get => _book.SequenceTimeout;
        set => _book.SequenceTimeout = value;
    }

    internal int RegistrationCount
    {
        get { lock (_bookLock) return _ids.Count; }
    }

    internal nint Window
    {
        get { lock (_bookLock) return _hwnd; }
    }

    internal bool NeedsHook
    {
        get { lock (_bookLock) return _book.NeedsHook; }
    }

    internal static bool IsHookInstalled
    {
        get { lock (Gate) return Hook is not null; }
    }

    internal static Thread? InstalledHookThread
    {
        get { lock (Gate) return Hook?.Thread; }
    }

    public HotkeyClaim Claim(IHotkeyListener listener, HotkeySequence? sequence)
    {
        if (_disposed)
            return HotkeyClaim.Invalid("Hotkeys are shut down.");
        ReleaseListener(listener);
        if (sequence is null || sequence.Chords.Count == 0)
        {
            SyncHook();
            return HotkeyClaim.Idle;
        }
        var first = sequence.Chords[0];
        if (!first.IsWinOnly)
        {
            var exists = false;
            lock (_bookLock)
                exists = _book.HasOs(first.OsKey);
            if (!exists && !TryRegister(first, out var error, out var taken))
            {
                SyncHook();
                return taken ? HotkeyClaim.Taken(error) : HotkeyClaim.Invalid(error);
            }
        }
        lock (_bookLock)
            _book.Add(listener, sequence);
        if (first.IsWinOnly && InstallNativeHook)
        {
            SyncHook();
            if (!OwnsHook)
            {
                lock (_bookLock)
                    _book.Remove(listener);
                FlushRegistrations();
                return HotkeyClaim.Invalid("Windows did not install a keyboard hook, so the Windows key cannot be registered.");
            }
        }
        else
        {
            SyncHook();
        }
        return HotkeyClaim.Active;
    }

    public void Release(IHotkeyListener listener)
    {
        if (_disposed)
            return;
        ReleaseListener(listener);
        SyncHook();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        int[] ids;
        IDisposable[] timers;
        nint hwnd;
        GCHandle self;
        lock (_bookLock)
        {
            ids = [.. _ids.Values];
            timers = [.. _timers.Values];
            _ids.Clear();
            _chords.Clear();
            _timers.Clear();
            _pendingUnregister.Clear();
            _book.Clear();
            hwnd = _hwnd;
            _hwnd = 0;
            self = _self;
            _self = default;
        }
        foreach (var timer in timers)
            timer.Dispose();
        if (hwnd != 0)
        {
            foreach (var id in ids)
                Win32.UnregisterHotKey(hwnd, id);
            Win32.SetWindowLongPtr(hwnd, Win32.GWLP_USERDATA, 0);
            Win32.DestroyWindow(hwnd);
        }
        if (self.IsAllocated)
            self.Free();
        DetachHook();
    }

    /// <summary>Removes this process's hotkey hook, whoever installed it. Tests call this if a registrar is abandoned.</summary>
    internal static void EmergencyUnhook()
    {
        HookThread? hook;
        lock (Gate)
        {
            hook = Hook;
            Hook = null;
            HookOwner = null;
        }
        hook?.Stop();
    }

    internal bool TryGetId(OsChord chord, out int id)
    {
        lock (_bookLock)
            return _ids.TryGetValue(chord, out id);
    }

    internal void OnHotkey(int id)
    {
        BookResult result;
        var changed = false;
        lock (_bookLock)
        {
            if (_disposed || !_chords.TryGetValue(id, out var chord))
                return;
            var before = _book.NeedsHook;
            result = _book.Trigger(chord, NowMs());
            changed = before != _book.NeedsHook;
        }
        Deliver(result);
        if (changed)
            SyncHook();
    }

    /// <summary>Feeds one key into the book. Tests use this instead of injecting a keystroke.</summary>
    internal bool HandleKey(int virtualKey, bool isUp, HotkeyModifiers modifiers)
    {
        BookResult result;
        var changed = false;
        lock (_bookLock)
        {
            var before = _book.NeedsHook;
            result = _book.Hook(virtualKey, isUp, modifiers, NowMs());
            changed = before != _book.NeedsHook;
        }
        Deliver(result);
        if (changed)
            SyncHook();
        return result.Swallow;
    }

    private void ReleaseListener(IHotkeyListener listener)
    {
        CancelTimer(listener);
        lock (_bookLock)
            _book.Remove(listener);
        FlushRegistrations();
    }

    private bool TryRegister(HotkeyChord chord, out string error, out bool taken)
    {
        taken = false;
        error = "";
        if (!EnsureWindow())
        {
            error = $"{chord.Text} could not be registered (0).";
            return false;
        }
        int id;
        nint hwnd;
        lock (_bookLock)
        {
            if (_ids.ContainsKey(chord.OsKey))
                return true;
            if (_nextId > 0xBFFF)
            {
                error = $"{chord.Text} could not be registered (no id).";
                return false;
            }
            id = _nextId;
            hwnd = _hwnd;
        }
        if (!Win32.RegisterHotKey(hwnd, id, chord.Win32Modifiers | ModNoRepeat, (uint)chord.VirtualKey))
        {
            var code = Marshal.GetLastPInvokeError();
            taken = code == ErrorAlreadyRegistered;
            error = taken
                ? $"{chord.Text} is already in use by Windows or another app."
                : $"{chord.Text} could not be registered ({code}).";
            return false;
        }
        lock (_bookLock)
        {
            if (_disposed || _hwnd != hwnd)
            {
                Win32.UnregisterHotKey(hwnd, id);
                error = "Hotkeys are shut down.";
                return false;
            }
            if (_ids.ContainsKey(chord.OsKey))
            {
                Win32.UnregisterHotKey(hwnd, id);
                return true;
            }
            _ids[chord.OsKey] = id;
            _chords[id] = chord.OsKey;
            _nextId = id + 1;
            return true;
        }
    }

    private void FlushRegistrations()
    {
        int[] ids;
        nint hwnd;
        lock (_bookLock)
        {
            foreach (var (chord, id) in _ids)
            {
                if (!_book.HasOs(chord))
                    _pendingUnregister.Add(id);
            }
            if (_pendingUnregister.Count > 0)
            {
                foreach (var id in _pendingUnregister)
                {
                    OsChord? found = null;
                    foreach (var (chord, chordId) in _ids)
                    {
                        if (chordId == id)
                        {
                            found = chord;
                            break;
                        }
                    }
                    if (found is { } chordKey)
                        _ids.Remove(chordKey);
                    _chords.Remove(id);
                }
            }
            ids = [.. _pendingUnregister];
            _pendingUnregister.Clear();
            hwnd = _hwnd;
        }
        if (hwnd != 0)
        {
            foreach (var id in ids)
                Win32.UnregisterHotKey(hwnd, id);
        }
        DestroyWindowIfIdle();
    }

    private void DestroyWindowIfIdle()
    {
        nint hwnd = 0;
        GCHandle self = default;
        lock (_bookLock)
        {
            if (_ids.Count > 0 || _hwnd == 0)
                return;
            hwnd = _hwnd;
            _hwnd = 0;
            self = _self;
            _self = default;
        }
        if (hwnd != 0)
        {
            Win32.SetWindowLongPtr(hwnd, Win32.GWLP_USERDATA, 0);
            Win32.DestroyWindow(hwnd);
        }
        if (self.IsAllocated)
            self.Free();
    }

    private bool EnsureWindow()
    {
        lock (_bookLock)
        {
            if (_hwnd != 0)
                return true;
        }
        if (_disposed)
            return false;
        if (!_classRegistered && !RegisterClass())
            return false;
        _classRegistered = true;
        var hwnd = Win32.CreateWindowExW(0, ClassName, "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, Win32.GetModuleHandleW(null), 0);
        if (hwnd == 0)
            return false;
        var self = GCHandle.Alloc(this);
        Win32.SetWindowLongPtr(hwnd, Win32.GWLP_USERDATA, GCHandle.ToIntPtr(self));
        lock (_bookLock)
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

    private bool RegisterClass()
    {
        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &HotkeyProc,
                hInstance = Win32.GetModuleHandleW(null),
                lpszClassName = name,
            };
            if (Win32.RegisterClassExW(wc) != 0)
                return true;
            return Marshal.GetLastPInvokeError() == ErrorClassAlreadyExists;
        }
    }

    private void Deliver(BookResult result)
    {
        if (_disposed)
            return;
        foreach (var listener in result.Pressed)
        {
            try
            {
                listener.OnPressed(false);
            }
            catch (Exception e)
            {
                Log.Default.Warn("hotkey", e.Message);
            }
        }
        foreach (var arm in result.Armed)
            ScheduleDisarm(arm);
    }

    private void ScheduleDisarm(ArmNotice arm)
    {
        CancelTimer(arm.Listener);
        var listener = arm.Listener;
        var token = arm.Token;
        var delay = TimeSpan.FromMilliseconds(Math.Max(0, arm.DeadlineMs - NowMs()));
        var timer = listener.Loop.Schedule(delay, () => Disarm(listener, token));
        _timers[listener] = timer;
    }

    private void Disarm(IHotkeyListener listener, long token)
    {
        _timers.Remove(listener);
        if (_disposed)
            return;
        var changed = false;
        lock (_bookLock)
        {
            var before = _book.NeedsHook;
            _book.Disarm(listener, token);
            changed = before != _book.NeedsHook;
        }
        if (changed)
            SyncHook();
    }

    private void CancelTimer(IHotkeyListener listener)
    {
        if (_timers.Remove(listener, out var timer))
            timer.Dispose();
    }

    private long NowMs() => (long)_loop.Now.TotalMilliseconds;

    private bool OwnsHook
    {
        get { lock (Gate) return ReferenceEquals(HookOwner, this) && Hook is not null; }
    }

    private void SyncHook()
    {
        if (_disposed)
            return;
        if (NeedsHook && InstallNativeHook)
            AttachHook();
        else
            DetachHook();
    }

    // The procedure can run before the hook is recorded here. Do not hold the book lock, or the static gate, while waiting for it.
    private bool AttachHook()
    {
        lock (Gate)
        {
            if (HookOwner == this && Hook is not null)
                return true;
            if (HookOwner is not null && HookOwner != this)
                return false;
            HookOwner = this;
        }
        var hook = HookThread.Start(Win32.WH_KEYBOARD_LL, &KeyboardProc, "Winshell keyboard hook");
        lock (Gate)
        {
            if (HookOwner == this && hook is not null)
            {
                Hook = hook;
                return true;
            }
            if (HookOwner == this)
                HookOwner = null;
        }
        hook?.Stop();
        return false;
    }

    private void DetachHook()
    {
        HookThread? hook;
        lock (Gate)
        {
            if (HookOwner != this)
                return;
            hook = Hook;
            Hook = null;
            HookOwner = null;
        }
        hook?.Stop();
    }

    // The procedure runs on the hook thread. Work for the loop is posted, and so is the unhook.
    private bool OnHook(nint lParam)
    {
        var info = (KbdLlHookStruct*)lParam;
        BookResult result;
        var changed = false;
        lock (_bookLock)
        {
            if (_disposed)
                return false;
            var before = _book.NeedsHook;
            result = _book.Hook((int)info->vkCode, (info->flags & LlkhfUp) != 0, ReadModifiers(), NowMs());
            changed = before != _book.NeedsHook;
        }
        if (result.Pressed.Count > 0 || result.Armed.Count > 0)
        {
            var posted = result;
            _loop.Post(() => Deliver(posted));
        }
        if (changed)
            PostSync();
        return Consume(result);
    }

    /// <summary>
    /// A handled Windows-key tap is not eaten. The masking key is injected while the Windows key is still down,
    /// and the real key-up is passed on so the async key state clears.
    /// </summary>
    internal static bool Consume(BookResult result, Action? mask = null)
    {
        if (!result.MaskWin)
            return result.Swallow;
        (mask ?? KeyMask.Tap).Invoke();
        return false;
    }

    private void PostSync()
    {
        if (Interlocked.Exchange(ref _syncPosted, 1) == 1)
            return;
        _loop.Post(() =>
        {
            Interlocked.Exchange(ref _syncPosted, 0);
            if (!_disposed)
                SyncHook();
        });
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

    private static bool Down(int virtualKey) => Win32.GetAsyncKeyState(virtualKey) < 0;

    [UnmanagedCallersOnly]
    private static nint HotkeyProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == Win32.WM_HOTKEY)
        {
            NativeCallback.Guard(Log.Default, "hotkey", () =>
            {
                var data = Win32.GetWindowLongPtr(hwnd, Win32.GWLP_USERDATA);
                if (data != 0 && GCHandle.FromIntPtr(data).Target is WindowsHotkeyRegistrar registrar)
                    registrar._loop.Post(() => registrar.OnHotkey((int)wParam));
            });
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        var swallow = code >= 0 && !IsOwnInjection(lParam) && NativeCallback.Guard(Log.Default, "hotkey", () =>
        {
            WindowsHotkeyRegistrar? owner;
            lock (Gate)
                owner = HookOwner;
            return owner is not null && owner.OnHook(lParam);
        }, false);
        if (swallow)
            return 1;
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    private static bool IsOwnInjection(nint lParam)
    {
        if (lParam == 0)
            return false;
        return ((KbdLlHookStruct*)lParam)->dwExtraInfo == KeyMask.ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }
}
