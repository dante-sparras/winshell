using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Input;

/// <summary>
/// Registers hotkeys with <c>RegisterHotKey</c>, and installs one low-level keyboard hook while a Windows-key
/// hotkey is claimed, a key sequence is waiting for its next chord, or a shortcut inhibitor is capturing.
/// </summary>
internal sealed unsafe class WindowsHotkeyRegistrar : IHotkeyRegistrar, IDisposable
{
    private const int ErrorAlreadyRegistered = 1409;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint ModNoRepeat = 0x4000;
    private const uint LlkhfExtended = 0x01;
    private const uint LlkhfAltDown = 0x20;
    private const uint LlkhfUp = 0x80;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkCapsLock = 0x14;
    private const int VkDelete = 0x2E;
    private const int VkL = 0x4C;
    private const int VkNumLock = 0x90;
    private const int VkScrollLock = 0x91;
    private const int VkLeftShift = 0xA0;
    private const int VkRightAlt = 0xA5;
    private const int MaxTrackedWindows = 16;
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
    private readonly Dictionary<IInhibitClient, nint> _clients = [];
    private readonly bool[] _down = new bool[256];
    private int _syncPosted;
    private int _recheckPosted;
    private int _captureLive;
    private bool _classRegistered;
    private bool _disposed;

    public WindowsHotkeyRegistrar(IEventLoop loop) => _loop = loop;

    /// <summary>When false, a sequence can arm without installing a hook. Tests use this so a hook is never left behind.</summary>
    internal bool InstallNativeHook { get; set; } = true;

    /// <summary>When false, the hook is not installed. Tests use this to refuse a capture without calling Windows.</summary>
    internal bool AllowHookInstall { get; set; } = true;

    /// <summary>Whether <paramref name="hwnd"/> is the foreground window. Tests substitute a window that is not really in front.</summary>
    internal Func<nint, bool> IsForeground { get; set; } = static hwnd => hwnd != 0 && Win32.GetForegroundWindow() == hwnd;

    /// <summary>Whether Windows saw this key go down and has not seen it come up. Tests substitute it so the real keyboard is not read.</summary>
    internal Func<int, bool> HeldByWindows { get; set; } = Down;

    /// <summary>Posts one captured key to the shell window. Tests record the key instead of posting it.</summary>
    internal Func<PostedKey, bool> ForwardKey { get; set; } = PostToWindow;

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
        get { lock (_bookLock) return _book.NeedsHook || _clients.Count > 0; }
    }

    internal static bool IsHookInstalled
    {
        get { lock (Gate) return Hook is not null; }
    }

    internal static Thread? InstalledHookThread
    {
        get { lock (Gate) return Hook?.Thread; }
    }

    internal bool IsDisposed => _disposed;

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
        IInhibitClient[] clients;
        nint hwnd;
        GCHandle self;
        lock (_bookLock)
        {
            ids = [.. _ids.Values];
            timers = [.. _timers.Values];
            clients = [.. _clients.Keys];
            _ids.Clear();
            _chords.Clear();
            _timers.Clear();
            _pendingUnregister.Clear();
            _book.Clear();
            _clients.Clear();
            Array.Clear(_down);
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
        // The hook is gone. Tell inhibitors so active does not stay true, and do not report cancelled:
        // the shell asked for this stop.
        foreach (var client in clients)
        {
            try
            {
                client.Recheck();
            }
            catch (Exception e)
            {
                Log.Default.Warn("hotkey", e.Message);
            }
        }
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
    internal bool HandleKey(int virtualKey, bool isUp, HotkeyModifiers modifiers, int scanCode = 0, bool altDown = false, bool extended = false)
    {
        var flags = isUp ? LlkhfUp : 0;
        if (altDown)
            flags |= LlkhfAltDown;
        if (extended)
            flags |= LlkhfExtended;
        var decision = Decide(virtualKey, isUp, modifiers, (uint)scanCode, flags);
        if (decision == InhibitDecision.Swallow)
            return true;
        if (decision == InhibitDecision.Pass)
            return false;
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
        if (!AllowHookInstall)
            return false;
        lock (Gate)
        {
            if (HookOwner == this && Hook is not null)
                return true;
            if (HookOwner is not null && HookOwner != this)
                return false;
            HookOwner = this;
        }
        var hook = HookThread.Start(Win32.WH_KEYBOARD_LL, &KeyboardProc, "Winshell keyboard hook", OnHookThreadExited);
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
    // Read the modifiers before taking the book lock: GetAsyncKeyState does not need it, and the lock stays short.
    private bool OnHook(nint lParam)
    {
        var info = (KbdLlHookStruct*)lParam;
        var vk = (int)info->vkCode;
        var up = (info->flags & LlkhfUp) != 0;
        var mods = ReadModifiers();
        var decision = Decide(vk, up, mods, info->scanCode, info->flags);
        if (decision == InhibitDecision.Swallow)
            return true;
        if (decision == InhibitDecision.Pass)
            return false;
        BookResult result;
        var changed = false;
        lock (_bookLock)
        {
            if (_disposed)
                return false;
            var before = _book.NeedsHook;
            result = _book.Hook(vk, up, mods, NowMs());
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

    /// <summary>
    /// Captures keys for <paramref name="hwnd"/> while it is the foreground window. A zero handle stops.
    /// Returns false when Windows will not install the hook. The client is not left registered in that case.
    /// </summary>
    internal bool SetInhibition(IInhibitClient client, nint hwnd)
    {
        if (_disposed)
            return false;
        lock (_bookLock)
        {
            var had = _clients.Count > 0;
            if (hwnd == 0)
                _clients.Remove(client);
            else
                _clients[client] = hwnd;
            if (had != (_clients.Count > 0))
                _book.ForgetHeldKeys();
            if (_clients.Count == 0)
            {
                Array.Clear(_down);
                Interlocked.Exchange(ref _captureLive, 0);
            }
        }
        SyncHook();
        if (hwnd == 0 || !InstallNativeHook)
            return true;
        if (OwnsHook)
            return true;
        lock (_bookLock)
        {
            _clients.Remove(client);
            if (_clients.Count == 0)
            {
                _book.ForgetHeldKeys();
                Array.Clear(_down);
                Interlocked.Exchange(ref _captureLive, 0);
            }
        }
        SyncHook();
        return false;
    }

    /// <summary>
    /// The hook thread ended without being asked to. Windows does not report a hook it removed for a timeout
    /// while leaving the thread running, so this is only the exit this process can see.
    /// </summary>
    internal void NoteHookLost()
    {
        if (_disposed)
            return;
        IInhibitClient[] clients;
        lock (_bookLock)
        {
            clients = [.. _clients.Keys];
            _clients.Clear();
            Array.Clear(_down);
            Interlocked.Exchange(ref _captureLive, 0);
            if (clients.Length > 0)
                _book.ForgetHeldKeys();
        }
        foreach (var client in clients)
        {
            try
            {
                client.Cancelled();
            }
            catch (Exception e)
            {
                Log.Default.Warn("hotkey", e.Message);
            }
        }
        if (!_disposed)
            SyncHook();
    }

    private void OnHookThreadExited()
    {
        lock (Gate)
        {
            if (!ReferenceEquals(HookOwner, this))
                return;
            Hook = null;
            HookOwner = null;
        }
        _loop.Post(NoteHookLost);
    }

    private enum InhibitDecision
    {
        None,
        Swallow,
        Pass,
    }

    // Runs on the hook thread. No QML, no logging, and no wait: the system drops a hook that is slow.
    private InhibitDecision Decide(int virtualKey, bool isUp, HotkeyModifiers modifiers, uint scanCode, uint flags)
    {
        Span<nint> windows = stackalloc nint[MaxTrackedWindows];
        var count = 0;
        lock (_bookLock)
        {
            if (_disposed || _clients.Count == 0)
                return InhibitDecision.None;
            foreach (var hwnd in _clients.Values)
            {
                if (count == windows.Length)
                    break;
                windows[count++] = hwnd;
            }
        }
        nint target = 0;
        for (var i = 0; i < count; i++)
        {
            if (IsForeground(windows[i]))
            {
                target = windows[i];
                break;
            }
        }
        if (target == 0)
        {
            // Windows gets the key-ups from here on, so nothing held now is the window's to release.
            if (Interlocked.Exchange(ref _captureLive, 0) == 1)
            {
                lock (_bookLock)
                    Array.Clear(_down);
            }
            PostForegroundRecheck();
            return InhibitDecision.None;
        }
        // The first key back in front asks the loop to set active again. Later keys stay on this thread.
        if (Interlocked.Exchange(ref _captureLive, 1) == 0)
            PostForegroundRecheck();
        // Ctrl+Alt+Del and Win+L still reach Windows. Eating them would claim a block Windows does not allow.
        // The window reads these from its own key state, which a posted key does not change.
        // Held back, Shift+A would arrive as a and Ctrl+C as c.
        if (IsUnblockable(virtualKey, modifiers) || IsKeyStateKey(virtualKey))
            return InhibitDecision.Pass;
        var captured = false;
        if ((uint)virtualKey < (uint)_down.Length)
        {
            lock (_bookLock)
                captured = _down[virtualKey];
        }
        // Windows saw this key go down, so it has to see the key come up, or the key stays held for every app.
        if (!captured && isUp)
            return InhibitDecision.None;
        if (!captured && HeldByWindows(virtualKey))
            return InhibitDecision.Pass;
        return TryForward(target, virtualKey, isUp, captured, scanCode, flags) ? InhibitDecision.Swallow : InhibitDecision.None;
    }

    private bool IsUnblockable(int virtualKey, HotkeyModifiers modifiers)
    {
        if (virtualKey == VkDelete && (modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) == (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt))
            return true;
        if (virtualKey != VkL)
            return false;
        // A captured Windows key never reached Windows, so the modifiers read in the hook do not show it.
        if ((modifiers & HotkeyModifiers.Win) != 0)
            return true;
        lock (_bookLock)
            return _down[VirtualKeys.LeftWin] || _down[VirtualKeys.RightWin];
    }

    private static bool IsKeyStateKey(int virtualKey) =>
        virtualKey is VkShift or VkControl or VkAlt or VkCapsLock or VkNumLock or VkScrollLock
            or >= VkLeftShift and <= VkRightAlt;

    private bool TryForward(nint hwnd, int virtualKey, bool isUp, bool captured, uint scanCode, uint flags)
    {
        var known = (uint)virtualKey < (uint)_down.Length;
        var repeat = !isUp && captured;
        var alt = (flags & LlkhfAltDown) != 0;
        var extended = (flags & LlkhfExtended) != 0;
        uint message;
        if (isUp)
            message = alt ? Win32.WM_SYSKEYUP : Win32.WM_KEYUP;
        else
            message = alt ? Win32.WM_SYSKEYDOWN : Win32.WM_KEYDOWN;
        long lp = 1;
        lp |= (long)(scanCode & 0xFF) << 16;
        if (extended)
            lp |= 1L << 24;
        if (alt)
            lp |= 1L << 29;
        if (isUp || repeat)
            lp |= 1L << 30;
        if (isUp)
            lp |= 1L << 31;
        var posted = ForwardKey(new PostedKey(hwnd, message, virtualKey, (nint)lp));
        if (known)
        {
            lock (_bookLock)
                _down[virtualKey] = posted && !isUp;
        }
        return posted;
    }

    private static bool PostToWindow(PostedKey key) =>
        key.Hwnd != 0 && Win32.PostMessageW(key.Hwnd, key.Message, key.WParam, key.LParam);

    private void PostForegroundRecheck()
    {
        if (Interlocked.Exchange(ref _recheckPosted, 1) == 1)
            return;
        _loop.Post(() =>
        {
            Interlocked.Exchange(ref _recheckPosted, 0);
            if (_disposed)
                return;
            IInhibitClient[] clients;
            lock (_bookLock)
                clients = [.. _clients.Keys];
            foreach (var client in clients)
            {
                try
                {
                    client.Recheck();
                }
                catch (Exception e)
                {
                    Log.Default.Warn("hotkey", e.Message);
                }
            }
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
