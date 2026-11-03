using System.Runtime.CompilerServices;

namespace Winshell.Input;

/// <summary>
/// Connects the <see cref="HotkeyBook"/> of one loop to Windows. A normal chord goes to a <see cref="HotkeyWindow"/>;
/// the Windows key, and a sequence waiting for its next chord, hold the <see cref="KeyboardHook"/> while the book needs it.
/// </summary>
internal sealed class WindowsHotkeyRegistrar : IHotkeyRegistrar, IDisposable
{
    private static readonly ConditionalWeakTable<IEventLoop, WindowsHotkeyRegistrar> PerLoop = new();

    private readonly object _bookLock = new();
    private readonly IEventLoop _loop;
    private readonly HotkeyBook _book = new();
    private readonly HotkeyWindow _window;
    private readonly KeyboardHook _hook;
    private readonly Dictionary<IHotkeyListener, IDisposable> _timers = [];
    private int _syncPosted;
    private bool _disposed;

    public WindowsHotkeyRegistrar(IEventLoop loop)
    {
        _loop = loop;
        _window = new HotkeyWindow(id => _loop.Post(() => OnHotkey(id)));
        // The hook thread ended without being asked to. Windows does not report a hook it removed for a timeout
        // while leaving the thread running, so this is only the exit this process can see.
        _hook = new KeyboardHook(OnHook, () => _loop.Post(SyncHook));
    }

    /// <summary>The registrar the hotkeys on a loop share. With nothing claimed, it holds no window and no hook.</summary>
    public static WindowsHotkeyRegistrar For(IEventLoop loop) => PerLoop.GetValue(loop, static l => new WindowsHotkeyRegistrar(l));

    /// <summary>When false, a sequence can arm without installing a hook. Tests use this so a hook is never left behind.</summary>
    internal bool InstallNativeHook { get; set; } = true;

    internal int RegistrationCount => _window.Count;

    internal nint Window => _window.Handle;

    internal bool NeedsHook
    {
        get { lock (_bookLock) return _book.NeedsHook; }
    }

    internal static bool IsHookInstalled => KeyboardHook.IsInstalled;

    internal static Thread? InstalledHookThread => KeyboardHook.InstalledThread;

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
            bool exists;
            lock (_bookLock)
                exists = _book.HasOs(first.OsKey);
            if (!exists)
            {
                var registered = _window.Register(first);
                if (registered.Status != HotkeyClaimStatus.Active)
                {
                    SyncHook();
                    return registered;
                }
            }
        }
        lock (_bookLock)
            _book.Add(listener, sequence);
        SyncHook();
        if (first.IsWinOnly && InstallNativeHook && !_hook.IsAttached)
        {
            lock (_bookLock)
                _book.Remove(listener);
            FlushRegistrations();
            return HotkeyClaim.Invalid("Windows did not install a keyboard hook, so the Windows key cannot be registered.");
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
        IDisposable[] timers;
        lock (_bookLock)
        {
            timers = [.. _timers.Values];
            _timers.Clear();
            _book.Clear();
        }
        foreach (var timer in timers)
            timer.Dispose();
        _window.Dispose();
        _hook.Detach();
    }

    /// <summary>Removes this process's hotkey hook, whoever installed it. Tests call this if a registrar is abandoned.</summary>
    internal static void EmergencyUnhook() => KeyboardHook.EmergencyUnhook();

    internal bool TryGetId(OsChord chord, out int id) => _window.TryGetId(chord, out id);

    internal void OnHotkey(int id)
    {
        if (_disposed || !_window.TryGetChord(id, out var chord))
            return;
        BookResult result;
        var changed = false;
        lock (_bookLock)
        {
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

    private void ReleaseListener(IHotkeyListener listener)
    {
        CancelTimer(listener);
        lock (_bookLock)
            _book.Remove(listener);
        FlushRegistrations();
    }

    private void FlushRegistrations() => _window.UnregisterUnused(chord =>
    {
        lock (_bookLock)
            return _book.HasOs(chord);
    });

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

    private void SyncHook()
    {
        if (_disposed)
            return;
        if (NeedsHook && InstallNativeHook)
            _hook.Attach();
        else
            _hook.Detach();
    }

    // Runs on the hook thread. Work for the loop is posted, and so is the unhook.
    // No logging, and no wait: the system drops a hook that is slow.
    private bool OnHook(int virtualKey, bool isUp, HotkeyModifiers modifiers)
    {
        BookResult result;
        var changed = false;
        lock (_bookLock)
        {
            if (_disposed)
                return false;
            var before = _book.NeedsHook;
            result = _book.Hook(virtualKey, isUp, modifiers, NowMs());
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
}
