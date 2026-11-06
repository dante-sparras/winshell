using System.Runtime.CompilerServices;

namespace Winshell.Input;

/// <summary>
/// The claim rules for the hotkeys on one loop: chords are shared, an unused chord is given back, the keyboard hook
/// is installed only while a Windows-key hotkey or a waiting sequence needs it, and a sequence that is not finished
/// in time is dropped. Windows is reached through <see cref="IHotkeyBackend"/>.
/// </summary>
internal sealed class HotkeyRegistrar : IDisposable
{
    private static readonly ConditionalWeakTable<IEventLoop, HotkeyRegistrar> PerLoop = new();

    private readonly object _bookLock = new();
    private readonly IEventLoop _loop;
    private readonly IHotkeyBackend _backend;
    private readonly HotkeyBook _book = new();
    private readonly HashSet<OsChord> _registered = [];
    private readonly Dictionary<IHotkeyListener, IDisposable> _timers = [];
    private int _syncPosted;
    private bool _disposed;

    public HotkeyRegistrar(IEventLoop loop)
        : this(loop, new HotkeyBackend(loop))
    {
    }

    internal HotkeyRegistrar(IEventLoop loop, IHotkeyBackend backend)
    {
        _loop = loop;
        _backend = backend;
        _backend.ChordPressed += OnChord;
    }

    /// <summary>The registrar the hotkeys on a loop share. With nothing claimed, it holds no window and no hook.</summary>
    public static HotkeyRegistrar For(IEventLoop loop)
    {
        return PerLoop.GetValue(loop, static l => new HotkeyRegistrar(l));
    }

    internal bool NeedsHook
    {
        get
        {
            lock (_bookLock)
            {
                return _book.NeedsHook;
            }
        }
    }

    internal bool IsKeyDown(int virtualKey)
    {
        return _backend.IsKeyDown(virtualKey);
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
            bool exists;
            lock (_bookLock)
            {
                exists = _book.HasOs(first.OsKey);
            }

            if (!exists)
            {
                var registered = _backend.Register(first);
                if (registered.Status != HotkeyClaimStatus.Active)
                {
                    SyncHook();
                    return registered;
                }
            }
        }

        lock (_bookLock)
        {
            if (!first.IsWinOnly)
                _registered.Add(first.OsKey);
            _book.Add(listener, sequence);
        }

        SyncHook();
        if (first.IsWinOnly && !_backend.IsHookInstalled)
        {
            lock (_bookLock)
            {
                _book.Remove(listener);
            }

            FlushRegistrations();
            return HotkeyClaim.Invalid(
                "Windows did not install a keyboard hook, so the Windows key cannot be registered.");
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
        _backend.ChordPressed -= OnChord;
        IDisposable[] timers;
        lock (_bookLock)
        {
            timers = [.. _timers.Values];
            _timers.Clear();
            _book.Clear();
            _registered.Clear();
        }

        foreach (var timer in timers)
            timer.Dispose();
        _backend.Dispose();
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

    private void OnChord(OsChord chord)
    {
        if (_disposed)
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

    private void ReleaseListener(IHotkeyListener listener)
    {
        CancelTimer(listener);
        lock (_bookLock)
        {
            _book.Remove(listener);
        }

        FlushRegistrations();
    }

    private void FlushRegistrations()
    {
        List<OsChord> unused = [];
        lock (_bookLock)
        {
            foreach (var chord in _registered)
                if (!_book.HasOs(chord))
                    unused.Add(chord);
            foreach (var chord in unused)
                _registered.Remove(chord);
        }

        foreach (var chord in unused)
            _backend.Unregister(chord);
    }

    private void Deliver(BookResult result)
    {
        if (_disposed)
            return;
        foreach (var listener in result.Pressed)
            try
            {
                listener.OnPressed(false);
            }
            catch (Exception e)
            {
                Log.Default.Warn("hotkey", e.Message);
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

    private long NowMs()
    {
        return (long)_loop.Now.TotalMilliseconds;
    }

    private void SyncHook()
    {
        if (_disposed)
            return;
        if (NeedsHook)
            _backend.InstallHook(OnHook, OnHookLost);
        else
            _backend.RemoveHook();
    }

    private void OnHookLost()
    {
        _loop.Post(SyncHook);
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
