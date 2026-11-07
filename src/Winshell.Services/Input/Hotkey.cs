namespace Winshell.Input;

/// <summary>
/// A hotkey that works anywhere in Windows. <see cref="Sequence"/> uses Windows names: <c>Win</c>, <c>Ctrl</c>,
/// <c>Alt</c> and <c>Shift</c>. Dispose it to give the keys back.
/// </summary>
public sealed class Hotkey : ObservableObject, IHotkeyListener, IDisposable
{
    private const int PollIntervalMs = 16;
    private const int MaxPolls = 3750;

    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly HotkeyRegistrar _registrar;
    private string _sequence = "";
    private bool _enabled = true;
    private bool _active;
    private bool _taken;
    private bool _pressed;
    private string _error = "";
    private HotkeySequence? _parsed;
    private IDisposable? _poll;
    private int _polls;
    private bool _disposed;

    /// <param name="loop">The loop the hotkey lives on.</param>
    /// <param name="log">Where a hotkey that is not active is reported.</param>
    /// <param name="registrar">Claims the keys, and reads whether a key is down.</param>
    internal Hotkey(IEventLoop loop, Log log, HotkeyRegistrar registrar)
    {
        _loop = loop;
        _log = log;
        _registrar = registrar;
    }

    /// <summary>
    /// The keys, such as <c>Win+Ctrl+K</c>, or several chords separated by commas, such as <c>Win+Ctrl+K, C</c>.
    /// Empty claims nothing. Setting it claims the new keys from Windows.
    /// </summary>
    public string Sequence
    {
        get => _sequence;
        set
        {
            if (Set(ref _sequence, value))
                Apply();
        }
    }

    /// <summary>Whether the hotkey claims its keys. False gives them back. True by default.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
                Apply();
        }
    }

    /// <summary>Whether the keys are claimed and a press raises <see cref="Pressed"/>.</summary>
    public bool Active => _active;

    /// <summary>Whether Windows or another app already holds the keys, so this hotkey could not claim them.</summary>
    public bool Taken => _taken;

    /// <summary>True from the press until the keys of the last chord are up.</summary>
    public bool IsPressed => _pressed;

    /// <summary>
    /// Why the hotkey is not active, such as a sequence that does not parse or keys that are taken. Empty when it is
    /// active, disabled, or has no sequence.
    /// </summary>
    public string Error => _error;

    /// <summary>The last chord of the sequence went down.</summary>
    public event EventHandler? Pressed;

    /// <summary>The keys of the last chord came up.</summary>
    public event EventHandler? Released;

    IEventLoop IHotkeyListener.Loop => _loop;

    void IHotkeyListener.OnPressed(bool deliversRelease)
    {
        if (_disposed)
            return;
        if (Set(ref _pressed, true, nameof(IsPressed)))
            Pressed?.Invoke(this, EventArgs.Empty);
        if (!deliversRelease)
            StartPoll();
    }

    void IHotkeyListener.OnReleased()
    {
        StopPoll();
        if (Set(ref _pressed, false, nameof(IsPressed)))
            Released?.Invoke(this, EventArgs.Empty);
    }

    private void Apply()
    {
        if (_disposed)
            return;
        if (_pressed)
            ((IHotkeyListener)this).OnReleased();
        if (!HotkeySequence.TryParse(_sequence, out var parsed, out var parseError))
        {
            _parsed = null;
            _registrar.Release(this);
            SetStatus(false, false, parseError ?? "");
            return;
        }

        _parsed = parsed;
        if (!_enabled)
        {
            _registrar.Release(this);
            SetStatus(false, false, "");
            return;
        }

        var claim = _registrar.Claim(this, parsed);
        SetStatus(claim.Status == HotkeyClaimStatus.Active, claim.Status == HotkeyClaimStatus.Taken, claim.Error);
    }

    private void SetStatus(bool active, bool taken, string error)
    {
        Set(ref _active, active, nameof(Active));
        Set(ref _taken, taken, nameof(Taken));
        if (Set(ref _error, error, nameof(Error)) && error.Length > 0)
            _log.Warn("hotkey", error);
    }

    private void StartPoll()
    {
        StopPoll();
        _polls = 0;
        _poll = _loop.Schedule(TimeSpan.FromMilliseconds(PollIntervalMs), Tick);
    }

    private void Tick()
    {
        _poll = null;
        _polls++;
        if (!_pressed || _disposed)
            return;
        if (_polls >= MaxPolls || _parsed is null || !IsDown(_parsed.Chords[^1]))
        {
            ((IHotkeyListener)this).OnReleased();
            return;
        }

        _poll = _loop.Schedule(TimeSpan.FromMilliseconds(PollIntervalMs), Tick);
    }

    private void StopPoll()
    {
        _poll?.Dispose();
        _poll = null;
    }

    private bool IsDown(HotkeyChord chord)
    {
        if (chord.IsWinOnly)
            return chord.Side switch
            {
                WinKeySide.Left => Down(VirtualKeys.LeftWin),
                WinKeySide.Right => Down(VirtualKeys.RightWin),
                _ => Down(VirtualKeys.LeftWin) || Down(VirtualKeys.RightWin)
            };
        return Down(chord.VirtualKey) && (VirtualKeys.Held(Down) & chord.Modifiers) == chord.Modifiers;
    }

    private bool Down(int virtualKey)
    {
        return _registrar.IsKeyDown(virtualKey);
    }

    /// <summary>Gives the keys back to Windows. No event is raised after this.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopPoll();
        _pressed = false;
        _registrar.Release(this);
    }
}
