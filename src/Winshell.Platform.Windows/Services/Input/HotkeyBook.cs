namespace Winshell.Platform.Windows.Services.Input;

/// <summary>An armed later step. <see cref="DeadlineMs"/> is when it expires if the next chord never arrives.</summary>
internal readonly record struct ArmNotice(IHotkeyListener Listener, long Token, long DeadlineMs);

/// <summary>Who a chord or a key event woke, and whether the event should be swallowed.</summary>
internal sealed class BookResult
{
    public List<IHotkeyListener> Pressed { get; } = [];

    public List<ArmNotice> Armed { get; } = [];

    /// <summary>Eat the event. A handled Windows-key tap sets this and <see cref="MaskWin"/>; the hook masks instead of eating the key-up.</summary>
    public bool Swallow { get; set; }

    /// <summary>
    /// The Windows key came up and this shell handled the tap. Inject a masking key, then let the real key-up through.
    /// Eating that key-up leaves the key logically down, because the key-down already reached the system.
    /// </summary>
    public bool MaskWin { get; set; }
}

/// <summary>
/// Which listeners own which chords, and which key sequences are waiting for their next chord.
/// Windows registration lives outside this type.
/// </summary>
internal sealed class HotkeyBook
{
    private readonly Dictionary<OsChord, List<IHotkeyListener>> _os = [];
    private readonly Dictionary<WinKeySide, List<IHotkeyListener>> _win = [];
    private readonly Dictionary<IHotkeyListener, HotkeySequence> _sequences = [];
    private readonly List<Armed> _armed = [];
    private long _token;
    private bool _leftDown;
    private bool _rightDown;
    private bool _leftUsed;
    private bool _rightUsed;

    public TimeSpan SequenceTimeout { get; set; } = TimeSpan.FromSeconds(1);

    private long TimeoutMs => (long)SequenceTimeout.TotalMilliseconds;

    /// <summary>True while a Windows-key hotkey is claimed or a sequence is waiting for its next chord.</summary>
    public bool NeedsHook => _win.Count > 0 || _armed.Count > 0;

    public bool HasOs(OsChord chord) => _os.ContainsKey(chord);

    public void Add(IHotkeyListener listener, HotkeySequence sequence)
    {
        Remove(listener);
        _sequences[listener] = sequence;
        var first = sequence.Chords[0];
        if (first.IsWinOnly)
            AddTo(_win, first.Side, listener);
        else
            AddTo(_os, first.OsKey, listener);
    }

    public void Remove(IHotkeyListener listener)
    {
        RemoveFrom(_os, listener);
        RemoveFrom(_win, listener);
        _sequences.Remove(listener);
        _armed.RemoveAll(armed => armed.Listener == listener);
    }

    public void Clear()
    {
        _os.Clear();
        _win.Clear();
        _sequences.Clear();
        _armed.Clear();
        _leftDown = _rightDown = _leftUsed = _rightUsed = false;
    }

    public BookResult Trigger(OsChord chord, long nowMs)
    {
        var result = new BookResult();
        Expire(nowMs);
        if (_os.TryGetValue(chord, out var list))
            Fire([.. list], nowMs, result, skip: null);
        return result;
    }

    /// <summary>Drops one armed step when its token is still current. A newer step keeps its own token.</summary>
    public void Disarm(IHotkeyListener listener, long token) =>
        _armed.RemoveAll(armed => armed.Listener == listener && armed.Token == token);

    public BookResult Hook(int virtualKey, bool isUp, HotkeyModifiers modifiers, long nowMs)
    {
        var result = new BookResult();
        Expire(nowMs);
        if (virtualKey is VirtualKeys.LeftWin or VirtualKeys.RightWin)
        {
            HandleWin(virtualKey == VirtualKeys.LeftWin ? WinKeySide.Left : WinKeySide.Right, isUp, modifiers, nowMs, result);
            return result;
        }
        if (isUp || VirtualKeys.IsModifier(virtualKey))
        {
            if (!isUp)
                MarkWinUsed();
            return result;
        }
        MarkWinUsed();
        var matched = new List<Armed>();
        foreach (var armed in _armed)
        {
            var expect = armed.Sequence.Chords[armed.Next];
            if (!expect.IsWinOnly && expect.VirtualKey == virtualKey && expect.Modifiers == modifiers)
                matched.Add(armed);
        }
        _armed.Clear();
        if (matched.Count == 0)
            return result;
        foreach (var armed in matched)
            Continue(armed, nowMs, result);
        result.Swallow = true;
        return result;
    }

    private void HandleWin(WinKeySide side, bool isUp, HotkeyModifiers modifiers, long nowMs, BookResult result)
    {
        if (!isUp)
        {
            if (side == WinKeySide.Left)
            {
                if (_leftDown)
                    return;
                _leftDown = true;
                _leftUsed = false;
            }
            else
            {
                if (_rightDown)
                    return;
                _rightDown = true;
                _rightUsed = false;
            }
            return;
        }
        var used = side == WinKeySide.Left ? _leftUsed : _rightUsed;
        if (side == WinKeySide.Left)
            _leftDown = false;
        else
            _rightDown = false;
        // Other modifiers, or a key pressed while Windows was down, make this a modifier release.
        if (used || (modifiers & ~HotkeyModifiers.Win) != 0)
            return;
        // Finish a sequence that was waiting for this tap before starting one whose first chord is the tap.
        // Starting first would put the new sequence on the armed list, and finishing clears that list.
        var skip = AdvanceWin(side, nowMs, result);
        FireWin(side, nowMs, result, skip);
        FireWin(WinKeySide.Either, nowMs, result, skip);
        var handled = result.Pressed.Count > 0 || result.Armed.Count > 0;
        result.Swallow = handled;
        // The key-down was not eaten, so the key-up must reach Windows or the key stays held.
        // MaskWin tells the hook to inject an unused key first, which uses the press so Start stays closed.
        result.MaskWin = handled;
    }

    private HashSet<IHotkeyListener> AdvanceWin(WinKeySide side, long nowMs, BookResult result)
    {
        var matched = new List<Armed>();
        foreach (var armed in _armed)
        {
            var expect = armed.Sequence.Chords[armed.Next];
            if (expect.IsWinOnly && (expect.Side == WinKeySide.Either || expect.Side == side))
                matched.Add(armed);
        }
        _armed.Clear();
        var skip = new HashSet<IHotkeyListener>();
        foreach (var armed in matched)
        {
            skip.Add(armed.Listener);
            Continue(armed, nowMs, result);
        }
        return skip;
    }

    private void FireWin(WinKeySide side, long nowMs, BookResult result, HashSet<IHotkeyListener> skip)
    {
        if (_win.TryGetValue(side, out var list))
            Fire([.. list], nowMs, result, skip);
    }

    private void Fire(IReadOnlyList<IHotkeyListener> listeners, long nowMs, BookResult result, HashSet<IHotkeyListener>? skip)
    {
        foreach (var listener in listeners)
        {
            if (skip is not null && skip.Contains(listener))
                continue;
            if (!_sequences.TryGetValue(listener, out var sequence))
                continue;
            _armed.RemoveAll(armed => armed.Listener == listener);
            if (sequence.Chords.Count <= 1)
                result.Pressed.Add(listener);
            else
                StartArm(listener, sequence, nowMs, result);
        }
    }

    private void StartArm(IHotkeyListener listener, HotkeySequence sequence, long nowMs, BookResult result)
    {
        var armed = new Armed
        {
            Listener = listener,
            Sequence = sequence,
            Next = 1,
            Token = ++_token,
            Deadline = nowMs + TimeoutMs,
        };
        _armed.Add(armed);
        result.Armed.Add(new ArmNotice(listener, armed.Token, armed.Deadline));
    }

    private void Continue(Armed armed, long nowMs, BookResult result)
    {
        var next = armed.Next + 1;
        if (next >= armed.Sequence.Chords.Count)
        {
            result.Pressed.Add(armed.Listener);
            return;
        }
        armed.Next = next;
        armed.Token = ++_token;
        armed.Deadline = nowMs + TimeoutMs;
        _armed.Add(armed);
        result.Armed.Add(new ArmNotice(armed.Listener, armed.Token, armed.Deadline));
    }

    private void MarkWinUsed()
    {
        if (_leftDown)
            _leftUsed = true;
        if (_rightDown)
            _rightUsed = true;
    }

    private void Expire(long nowMs) => _armed.RemoveAll(armed => nowMs >= armed.Deadline);

    private static void AddTo<TKey>(Dictionary<TKey, List<IHotkeyListener>> map, TKey key, IHotkeyListener listener)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        list.Add(listener);
    }

    private static void RemoveFrom<TKey>(Dictionary<TKey, List<IHotkeyListener>> map, IHotkeyListener listener)
        where TKey : notnull
    {
        List<TKey>? empty = null;
        foreach (var (key, list) in map)
        {
            list.Remove(listener);
            if (list.Count == 0)
                (empty ??= []).Add(key);
        }
        if (empty is null)
            return;
        foreach (var key in empty)
            map.Remove(key);
    }

    private sealed class Armed
    {
        public required IHotkeyListener Listener { get; init; }

        public required HotkeySequence Sequence { get; init; }

        public int Next { get; set; }

        public long Token { get; set; }

        public long Deadline { get; set; }
    }
}
