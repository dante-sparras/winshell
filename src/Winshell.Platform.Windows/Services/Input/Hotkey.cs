using Winshell.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Input;

/// <summary>
/// A hotkey that works anywhere in Windows. <c>sequence</c> uses Windows names: <c>Win</c>, <c>Ctrl</c>, <c>Alt</c> and <c>Shift</c>.
/// </summary>
[QmlSignal("pressed")]
[QmlSignal("released")]
public sealed class Hotkey : QObject, IHotkeyListener
{
    private const int PollIntervalMs = 16;
    private const int MaxPolls = 3750;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    private string _sequence = "";
    private bool _enabled = true;
    private bool _active;
    private bool _taken;
    private bool _pressed;
    private string _error = "";
    private HotkeySequence? _parsed;
    private IDisposable? _poll;
    private int _polls;

    [Qml]
    public string Sequence
    {
        get { Track(); return _sequence; }
        set
        {
            if (SetField(ref _sequence, value ?? ""))
                Apply();
        }
    }

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set
        {
            if (SetField(ref _enabled, value))
                Apply();
        }
    }

    [Qml]
    public bool Active { get { Track(); return _active; } }

    [Qml]
    public bool Taken { get { Track(); return _taken; } }

    [Qml]
    public bool Pressed { get { Track(); return _pressed; } }

    [Qml]
    public string Error { get { Track(); return _error; } }

    public override void ComponentComplete() => Apply();

    protected override void OnDestroy()
    {
        StopPoll();
        _pressed = false;
        Engine?.GetService<IHotkeyRegistrar>()?.Release(this);
    }

    IEventLoop IHotkeyListener.Loop => Engine?.Loop ?? throw new InvalidOperationException("This hotkey is not in a shell.");

    void IHotkeyListener.OnPressed(bool deliversRelease)
    {
        if (SetField(ref _pressed, true, nameof(Pressed)))
            Emit("pressed");
        if (!deliversRelease)
            StartPoll();
    }

    void IHotkeyListener.OnReleased()
    {
        StopPoll();
        if (!SetField(ref _pressed, false, nameof(Pressed)))
            return;
        Emit("released");
    }

    private void Apply()
    {
        if (!IsComplete)
            return;
        if (_pressed)
            ((IHotkeyListener)this).OnReleased();
        if (!HotkeySequence.TryParse(_sequence, out var parsed, out var parseError))
        {
            _parsed = null;
            ReleaseClaim();
            SetStatus(active: false, taken: false, error: parseError ?? "");
            return;
        }
        _parsed = parsed;
        if (!_enabled)
        {
            ReleaseClaim();
            SetStatus(false, false, "");
            return;
        }
        var registrar = Engine?.GetService<IHotkeyRegistrar>();
        if (registrar is null)
        {
            SetStatus(false, false, "Hotkeys are not available.");
            return;
        }
        switch (registrar.Claim(this, parsed))
        {
            case { Status: HotkeyClaimStatus.Active }:
                SetStatus(true, false, "");
                break;
            case { Status: HotkeyClaimStatus.Taken } taken:
                SetStatus(false, true, taken.Error);
                break;
            case { Status: HotkeyClaimStatus.Invalid } invalid:
                SetStatus(false, false, invalid.Error);
                break;
            default:
                SetStatus(false, false, "");
                break;
        }
    }

    private void ReleaseClaim() => Engine?.GetService<IHotkeyRegistrar>()?.Release(this);

    private void SetStatus(bool active, bool taken, string error)
    {
        SetField(ref _active, active, nameof(Active));
        SetField(ref _taken, taken, nameof(Taken));
        if (!SetField(ref _error, error, nameof(Error)) || error.Length == 0)
            return;
        (Engine?.Log ?? Log.Default).Warn("hotkey", error);
    }

    private void StartPoll()
    {
        StopPoll();
        _polls = 0;
        var loop = Engine?.Loop;
        if (loop is null)
            return;
        _poll = loop.Schedule(TimeSpan.FromMilliseconds(PollIntervalMs), Tick);
    }

    private void Tick()
    {
        _poll = null;
        _polls++;
        if (!_pressed || IsDestroyed)
            return;
        if (_polls >= MaxPolls || _parsed is null || !IsDown(_parsed.Chords[^1]))
        {
            ((IHotkeyListener)this).OnReleased();
            return;
        }
        var loop = Engine?.Loop;
        if (loop is null)
            return;
        _poll = loop.Schedule(TimeSpan.FromMilliseconds(PollIntervalMs), Tick);
    }

    private void StopPoll()
    {
        _poll?.Dispose();
        _poll = null;
    }

    private static bool IsDown(HotkeyChord chord)
    {
        if (chord.IsWinOnly)
        {
            return chord.Side switch
            {
                WinKeySide.Left => Down(VirtualKeys.LeftWin),
                WinKeySide.Right => Down(VirtualKeys.RightWin),
                _ => Down(VirtualKeys.LeftWin) || Down(VirtualKeys.RightWin),
            };
        }
        if (!Down(chord.VirtualKey))
            return false;
        if ((chord.Modifiers & HotkeyModifiers.Ctrl) != 0 && !Down(VkControl))
            return false;
        if ((chord.Modifiers & HotkeyModifiers.Alt) != 0 && !Down(VkMenu))
            return false;
        if ((chord.Modifiers & HotkeyModifiers.Shift) != 0 && !Down(VkShift))
            return false;
        if ((chord.Modifiers & HotkeyModifiers.Win) != 0 && !Down(VirtualKeys.LeftWin) && !Down(VirtualKeys.RightWin))
            return false;
        return true;
    }

    private static bool Down(int virtualKey) => Win32.GetAsyncKeyState(virtualKey) < 0;
}

/// <summary>The <c>Winshell.Input</c> module.</summary>
public static class InputModule
{
    public static void Register(ModuleRegistry registry) => registry.Module("Winshell.Input").Type<Hotkey>().Type<ShortcutInhibitor>();
}
