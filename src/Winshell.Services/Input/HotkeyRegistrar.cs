namespace Winshell.Input;

/// <summary>
/// The claim rules for a shell's hotkeys, on its loop: chords are shared, an unused chord is given back, the keyboard hook
/// is installed only while a Windows-key hotkey or a waiting sequence needs it, and a sequence that is not finished
/// in time is dropped. Windows is reached through <see cref="IHotkeyBackend"/>.
/// </summary>
internal sealed class HotkeyRegistrar : IDisposable
{
    private readonly object _bookLock = new();
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly IHotkeyBackend _backend;
    private readonly HotkeyBook _book = new();
    private readonly long _started;
    private readonly Dictionary<IHotkeyListener, IDisposable> _timers = [];
    private int _syncPosted;
    private bool _disposed;

    /// <param name="loop">The loop the hotkeys live on.</param>
    /// <param name="log">Where a hotkey whose handler throws, and a failed Windows callback, are reported.</param>
    public HotkeyRegistrar(IEventLoop loop, Log log)
        : this(loop, log, new HotkeyBackend(loop, e => log.Warn("hotkey", e.Message)))
    {
    }

    internal HotkeyRegistrar(IEventLoop loop, Log log, IHotkeyBackend backend)
    {
        _loop = loop;
        _log = log;
        _started = loop.Time.GetTimestamp();
        _backend = backend;
        _backend.ChordPressed += OnChord;
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
            _book.Add(listener, sequence);
        }

        if (!SyncHook() && first.IsWinOnly)
        {
            lock (_bookLock)
            {
                _book.Remove(listener);
            }

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
        }

        foreach (var timer in timers)
            timer.Dispose();
        _backend.Dispose();
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
        OsChord? unused;
        lock (_bookLock)
        {
            unused = _book.Remove(listener);
        }

        if (unused is { } chord)
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
                _log.Warn("hotkey", e.Message);
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
        return (long)_loop.Time.GetElapsedTime(_started).TotalMilliseconds;
    }

    // True when the hook is installed.
    private bool SyncHook()
    {
        if (_disposed)
            return false;
        if (NeedsHook)
            return _backend.InstallHook(OnHook, () => SyncHook());
        _backend.RemoveHook();
        return false;
    }

    // Runs on the hook thread, which Windows drops when it is slow: no wait, and work for the loop is posted, the
    // unhook too. The modifiers are read before the lock, to keep it short.
    private bool OnHook(int virtualKey, bool isUp)
    {
        var modifiers = VirtualKeys.Held(_backend.IsKeyDown);
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
        if (!result.MaskWin)
            return result.Swallow;
        // A handled Windows-key tap is masked while the key is still down, and its key-up is passed on so the key
        // does not stay held.
        _backend.SendMask();
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
}
