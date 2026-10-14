using Winland.Ipc;

namespace Winshell.Core;

/// <summary>
/// The flags an extension adds to the shell's push to Winland.
/// The shell owns the bar windows, the edge, and the keyboard flag. The extension owns the rest.
/// </summary>
public readonly record struct ShellClaim
{
    /// <summary>Creates a claim. A null key list is empty.</summary>
    /// <param name="pickerOpen">True while a picker holds the keyboard.</param>
    /// <param name="escape">True while Escape is claimed.</param>
    /// <param name="sessionLocked">True while the session is locked.</param>
    /// <param name="keys">Keys the extension claims, such as the capture picker.</param>
    public ShellClaim(bool pickerOpen, bool escape, bool sessionLocked, IReadOnlyList<ClaimedKey>? keys)
    {
        PickerOpen = pickerOpen;
        Escape = escape;
        SessionLocked = sessionLocked;
        Keys = keys ?? [];
    }

    /// <summary>Nothing claimed.</summary>
    public static ShellClaim None { get; } = new(false, false, false, []);

    /// <summary>True while a picker holds the keyboard.</summary>
    public bool PickerOpen { get; }

    /// <summary>True while Escape is claimed.</summary>
    public bool Escape { get; }

    /// <summary>True while the session is locked.</summary>
    public bool SessionLocked { get; }

    /// <summary>Keys the extension claims.</summary>
    public IReadOnlyList<ClaimedKey> Keys { get; }
}

/// <summary>
/// What an extension does for the shell that the shell cannot do itself.
/// A null delegate is the shell's own answer: no seek, no focus, the screen is unknown, nothing is covered,
/// a chord or a notice command is ignored, the claim is <see cref="ShellClaim.None"/>, and the plugins are not paused.
/// The shell calls <see cref="MoveEdge"/>, <see cref="MoveItem"/>, and <see cref="ToggleTransparent"/> and then reloads settings.
/// Notifications are not a hand: a plugin calls <see cref="IPluginContext.ShowToast"/> and an extension calls <see cref="IShellHost.ShowNotice"/>.
/// </summary>
public sealed class SurfaceHands
{
    /// <summary>No hands. The shell draws with what it has.</summary>
    public static SurfaceHands None { get; } = new();

    /// <summary>
    /// Gives a window the keyboard the way a Menu retry does, with no activate backoff.
    /// Null when the extension has no gateway.
    /// </summary>
    public Func<nint, bool>? Seek { get; init; }

    /// <summary>Activates a window. Null when the extension has no gateway.</summary>
    public Func<nint, bool>? Focus { get; init; }

    /// <summary>The monitor that holds the keyboard, in physical pixels. Null when the extension does not know.</summary>
    public Func<ShellRect>? KeyboardScreen { get; init; }

    /// <summary>True when a fullscreen window covers the monitor it is given, so that bar yields. The rectangle is one monitor, in physical pixels.</summary>
    public Func<ShellRect, bool>? Covered { get; init; }

    /// <summary>Runs a host chord, such as an indicator click that is not a plugin command.</summary>
    public Action<string>? RunChord { get; init; }

    /// <summary>The extension's claim for the next push.</summary>
    public Func<ShellClaim>? Claim { get; init; }

    /// <summary>True while plugins should not be polled.</summary>
    public Func<bool>? Paused { get; init; }

    /// <summary>
    /// Receives the shell's <c>TouchLater</c>, so a read that lands off the UI thread can make a plugin due.
    /// The shell calls this once, after plugins mount.
    /// </summary>
    public Action<Action<string>>? BindTouch { get; init; }

    /// <summary>Writes a new bar edge. The shell reloads after it returns.</summary>
    public Action<BarEdge>? MoveEdge { get; init; }

    /// <summary>Writes one item's new place in the bar. The shell reloads after it returns.</summary>
    public Action<string, BarDropTarget>? MoveItem { get; init; }

    /// <summary>Writes the bar's transparent flag the other way. The shell reloads after it returns.</summary>
    public Action? ToggleTransparent { get; init; }

    /// <summary>
    /// The plugin whose panel is the Menu, opened at the middle of the monitor.
    /// Empty leaves the choice to a plugin whose kinds include <c>menu</c>.
    /// </summary>
    public string? MenuPluginId { get; init; }

    /// <summary>Runs a command from a notice the extension showed. False when the extension did not take it.</summary>
    public Func<string, bool>? NoticeCommand { get; init; }

    /// <summary>True while idle lock is on. Asked on a tick, so it has to be cheap. Null is off.</summary>
    public Func<bool>? IdleLock { get; init; }

    /// <summary>True while a recording is running. Asked on a tick, so it has to be cheap. Null is off.</summary>
    public Func<bool>? Recording { get; init; }

    /// <summary>A button on the recording notice. False leaves the notice to the shell, which hides it on close.</summary>
    public Func<string, bool>? RecordingAct { get; init; }

    /// <summary>Which monitor a bar's screen is, as the desktop numbers them. Null is the first.</summary>
    public Func<ShellRect, int>? MonitorAt { get; init; }

    /// <summary>A plugin changed something on screen, so the extension can pick up its pace.</summary>
    public Action? Activity { get; init; }

    /// <summary>
    /// True while a workspace recount is pending. The shell keeps the fast pace for as long as this says so (ADR 0029).
    /// Null is not pending.
    /// </summary>
    public Func<bool>? HoldFast { get; init; }

    /// <summary>The open panel changed. Null means none is open. The shell has already pushed.</summary>
    public Action<string?>? PanelChanged { get; init; }

    /// <summary>A plugin took or released the Escape claim.</summary>
    public Action<bool>? EscapeChanged { get; init; }

    /// <summary>
    /// Layers one bar: the window that covers its monitor, and whether the bar stays in front.
    /// Null leaves the bar above other windows. The shell stores the result and keeps the panel with the bar.
    /// </summary>
    public Func<BarLayerQuery, BarYield>? Yield { get; init; }

    /// <summary>Seeks <paramref name="window"/>. False when <see cref="Seek"/> is null or it declined.</summary>
    /// <param name="window">The window handle.</param>
    /// <returns>True when the keyboard moved.</returns>
    public bool TrySeek(nint window) => Seek?.Invoke(window) ?? false;

    /// <summary>Focuses <paramref name="window"/>. False when <see cref="Focus"/> is null or it declined.</summary>
    /// <param name="window">The window handle.</param>
    /// <returns>True when the window was activated.</returns>
    public bool TryFocus(nint window) => Focus?.Invoke(window) ?? false;

    /// <summary>The monitor that holds the keyboard, or null when <see cref="KeyboardScreen"/> is null.</summary>
    /// <returns>The monitor, in physical pixels.</returns>
    public ShellRect? ScreenWithKeyboard() => KeyboardScreen?.Invoke();

    /// <summary>True when <see cref="Covered"/> says <paramref name="screen"/> is covered.</summary>
    /// <param name="screen">One monitor, in physical pixels.</param>
    /// <returns>False when <see cref="Covered"/> is null.</returns>
    public bool IsCovered(ShellRect screen) => Covered?.Invoke(screen) ?? false;

    /// <summary>Runs <paramref name="chord"/> when <see cref="RunChord"/> is set.</summary>
    /// <param name="chord">The chord id.</param>
    public void TryRunChord(string chord) => RunChord?.Invoke(chord);

    /// <summary>The claim for the next push. <see cref="ShellClaim.None"/> when <see cref="Claim"/> is null.</summary>
    /// <returns>The claim.</returns>
    public ShellClaim CurrentClaim() => Claim?.Invoke() ?? ShellClaim.None;

    /// <summary>True when <see cref="Paused"/> says polling should wait.</summary>
    /// <returns>False when <see cref="Paused"/> is null.</returns>
    public bool IsPaused() => Paused?.Invoke() ?? false;

    /// <summary>True when <see cref="HoldFast"/> says a recount is pending.</summary>
    /// <returns>False when <see cref="HoldFast"/> is null.</returns>
    public bool IsHeldFast() => HoldFast?.Invoke() ?? false;

    /// <summary>Hands the shell's touch callback to <see cref="BindTouch"/> once.</summary>
    /// <param name="touchLater">Makes one plugin due on the next turn.</param>
    public void BindTouchLater(Action<string> touchLater) => BindTouch?.Invoke(touchLater);

    /// <summary>Asks the extension to write <paramref name="edge"/>.</summary>
    /// <param name="edge">The edge.</param>
    public void RequestEdge(BarEdge edge) => MoveEdge?.Invoke(edge);

    /// <summary>Asks the extension to write <paramref name="id"/> at <paramref name="target"/>.</summary>
    /// <param name="id">The plugin id.</param>
    /// <param name="target">Where it was dropped.</param>
    public void RequestMove(string id, BarDropTarget target) => MoveItem?.Invoke(id, target);

    /// <summary>Asks the extension to flip the bar's transparent flag.</summary>
    public void RequestTransparent() => ToggleTransparent?.Invoke();

    /// <summary>Offers <paramref name="command"/> to <see cref="NoticeCommand"/>.</summary>
    /// <param name="command">The notice command.</param>
    /// <returns>False when <see cref="NoticeCommand"/> is null or it declined.</returns>
    public bool TryNotice(string command) => NoticeCommand?.Invoke(command) ?? false;
}
