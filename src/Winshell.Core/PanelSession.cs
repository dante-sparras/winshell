using Winshell.Core.Diagnostics;

namespace Winshell.Core;

/// <summary>What a pointer-up on a slot asks of the bar.</summary>
/// <param name="ReleaseCapture">True when the slot captured the pointer and should let it go.</param>
/// <param name="Click">True when this release is the plugin's left click.</param>
public readonly record struct SlotRelease(bool ReleaseCapture, bool Click);

/// <summary>What a press on a Bar itself, outside every slot, did to the panel that was up.</summary>
/// <param name="Closed">The panel this press closes, or null when none was up. The click of this press must not open that panel again.</param>
/// <param name="Only">True when the press was on another monitor's Bar than the panel's. That press only dismisses, so its click opens nothing.</param>
public readonly record struct BarPress(string? Closed, bool Only);

/// <summary>
/// Decides when a panel opens, closes, or switches, and carries that out on its <see cref="IPanelWindow"/>.
/// One click on a slot toggles once. A click that closes is carried out here. A click that opens is answered as the plugin's
/// left click, because the plugin opens its own panel. The click that opened a panel does not also dismiss it.
/// A close that an event notes runs on the window's next turn, so a show that comes first cancels it.
/// One panel is up at a time, across every monitor, and it is on one Bar: <see cref="BarFor"/> says which a new one opens on.
/// A click on that Bar switches panels in one click. A click on another monitor's Bar only dismisses, as a click anywhere else there does.
/// A Workspace switch does not come here: the panel is Winoma's own window, which no switch hides, and it keeps the keyboard through one (ADR 0035).
/// Call every member on the UI thread.
/// </summary>
public sealed class PanelSession
{
    private readonly IPanelWindow _window;
    private readonly Dictionary<string, Contact> _contacts = new(StringComparer.Ordinal);
    private string? _openId;
    private int _bar;
    private bool _live;
    private bool _hiding;
    private int _generation;
    private int _opens;
    private int _armedGeneration;
    private int _accepting;
    private int _dismissHold;
    private int _suspend;
    private int _leaves;

    /// <summary>Drives <paramref name="window"/>.</summary>
    /// <param name="window">The window the panel is drawn in.</param>
    public PanelSession(IPanelWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    /// <summary>The plugin whose panel opened, or null when it closed. A refresh of the open panel is not a change.</summary>
    public event Action<string?>? OpenChanged;

    /// <summary>The plugin whose panel is on screen, or null after the hide has started.</summary>
    public string? OpenId => _openId;

    /// <summary>The Bar the panel is on, as an index into the Bars in the order they were made. Zero while none is open.</summary>
    public int Bar => _openId is null ? 0 : _bar;

    /// <summary>
    /// Which Bar <paramref name="id"/>'s panel opens on. One opened by a click opens under the Bar that was clicked.
    /// One nothing on a Bar asked for, as a Chord, Tab, and <c>winoma summon</c> open it, opens where a panel is already open,
    /// and otherwise on the monitor with the keyboard. A panel that is refreshed stays where it is.
    /// </summary>
    /// <param name="id">Plugin whose panel is about to be shown.</param>
    /// <param name="clicked">The Bar whose item or surface was clicked, or null when no click asked for the panel.</param>
    /// <param name="keyboard">The Bar on the monitor with the keyboard.</param>
    /// <returns>The index of the Bar to show it on.</returns>
    public int BarFor(string id, int? clicked, int keyboard)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_live && _openId is not null && (clicked is null || string.Equals(_openId, id, StringComparison.Ordinal)))
        {
            return _bar;
        }

        return clicked ?? keyboard;
    }

    /// <summary>True while the panel still accepts dismiss and content updates. False once a close has been noted, even if the window has not hidden yet.</summary>
    public bool Live => _live;

    /// <summary>
    /// Shows <paramref name="id"/>'s panel. <paramref name="draw"/> fills the window; it hears true when this refreshes the panel already open.
    /// A new open raises the window unseen, raises it again on the next turn, and starts hearing outside presses.
    /// Once the window has drawn the panel, it is revealed.
    /// A refresh starts no open again, but a deactivation closes the panel only after its window has activated once more.
    /// </summary>
    /// <param name="id">Plugin whose panel is drawn.</param>
    /// <param name="draw">Draws the panel. A new open stops any slide the window was running.</param>
    /// <param name="bar">The Bar it is shown on, from <see cref="BarFor"/>.</param>
    public void Show(string id, Action<bool> draw, int bar = 0)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(draw);
        var refreshing = _live && string.Equals(_openId, id, StringComparison.Ordinal);
        _openId = id;
        _bar = bar;
        _live = true;
        _generation++;
        if (!refreshing)
        {
            // Drawing a new open stops the leave slide, and that slide never reports its end.
            _hiding = false;
            _opens++;
        }

        draw(refreshing);
        if (refreshing)
        {
            return;
        }

        // Dismiss is ignored while the window is activating.
        _accepting++;
        try
        {
            _window.Raise(first: true);
        }
        finally
        {
            _accepting--;
        }

        // The window is raised unseen. Windows would otherwise show it before its rows are drawn, as a black box.
        // The open is counted apart from the shows. A refresh that came before the reveal would otherwise end this open's
        // reveal and second raise, and the panel would stay unseen with the keyboard.
        var open = _opens;
        _window.WhenDrawn(() =>
        {
            if (!StillCurrent(open))
            {
                return;
            }

            _window.Reveal();
        });

        // The opening press is on the bar, which refuses activation. Raise again after that
        // press returns, or Windows puts the previous window back in front and a later outside
        // click never deactivates this one.
        _window.Post(() =>
        {
            if (StillCurrent(open))
            {
                _window.Raise(first: false);
            }
        });
        _window.ListenOutside();
        OpenChanged?.Invoke(id);
    }

    /// <summary>Closes the panel now. A leave slide that is still running keeps this from starting another.</summary>
    public void Close()
    {
        if (_hiding)
        {
            return;
        }

        var id = _openId;
        _live = false;
        _openId = null;
        var leave = ++_leaves;
        _hiding = true;
        if (!_window.Leave(() => EndLeave(leave)))
        {
            _hiding = false;
        }

        if (id is not null)
        {
            OpenChanged?.Invoke(null);
        }
    }

    /// <summary>Escape was pressed in the panel. It closes at once unless the panel is already leaving.</summary>
    public void PressEscape()
    {
        if (_hiding || (_openId is null && !_live))
        {
            return;
        }

        Close();
    }

    /// <summary>The panel window gained activation. That arms this open, so a later deactivation can close it, and the panel goes back under the bar.</summary>
    public void WindowActivated()
    {
        if (!_live || _accepting > 0)
        {
            return;
        }

        _armedGeneration = _generation;
        _window.KeepUnderBar();
    }

    /// <summary>The panel window lost activation. That closes on the next turn, unless it is the click that opened the panel.</summary>
    /// <param name="buttonDownOnBar">True while the left button is down on the bar that opened the panel.</param>
    public void WindowDeactivated(bool buttonDownOnBar)
    {
        if (_dismissHold > 0 || _suspend > 0 || buttonDownOnBar || _armedGeneration != _generation)
        {
            return;
        }

        DismissLater();
    }

    /// <summary>
    /// A press anywhere on screen. A click outside the panel and the bar closes on the next turn, and it does not wait until the panel has activated.
    /// The bar does not activate, so it reports its own presses through <see cref="BarPressed"/>.
    /// </summary>
    /// <param name="insidePanel">True when the point is inside the panel window.</param>
    /// <param name="insideBar">True when the point is inside the bar window.</param>
    public void PressedOutside(bool insidePanel, bool insideBar)
    {
        if (insidePanel || insideBar || _suspend > 0)
        {
            return;
        }

        DismissLater();
    }

    /// <summary>A press on a Bar outside every slot. It closes on the next turn.</summary>
    /// <param name="bar">The Bar that was pressed.</param>
    /// <returns>What the press closes, and whether its click may open a panel.</returns>
    public BarPress BarPressed(int bar = 0)
    {
        var closing = _live && _accepting == 0 ? _openId : null;
        var only = Elsewhere(bar);
        DismissLater();
        return new BarPress(closing, only);
    }

    /// <summary>
    /// A pointer pressed <paramref name="id"/>.
    /// A left press on the control which opened the panel closes it on this down.
    /// Any other press closes a panel that is already up on the next turn, and the click comes on the release.
    /// </summary>
    /// <param name="id">Plugin id of the slot.</param>
    /// <param name="left">True when the left button is down.</param>
    /// <param name="onChild">True when the press landed on a button inside the slot.</param>
    /// <param name="onOpener">True when the press is on the control that opened this panel.</param>
    /// <param name="passThrough">True when this press must not close the open panel. A non-left press on the tray opens the manager.</param>
    /// <param name="bar">The Bar the slot is on. A press on another monitor's Bar than the open panel's only dismisses: its release is no click.</param>
    /// <returns>True when the slot should capture the pointer so the release still arrives. A repeat down is false.</returns>
    public bool PointerDown(string id, bool left, bool onChild, bool onOpener, bool passThrough, int bar = 0)
    {
        ArgumentNullException.ThrowIfNull(id);
        var contact = ContactFor(id);
        if (contact.Press.Down)
        {
            return false;
        }

        contact.Press.Begin();
        Hold();
        if (Elsewhere(bar))
        {
            // The click is spent on the dismissal. Its release and its tap open nothing, on this slot or as this panel's own toggle.
            contact.ReleaseOpens = false;
            contact.Press.Spend();
            DismissLater();
            return false;
        }

        contact.ReleaseOpens = left && !onChild;
        if (onOpener)
        {
            if (left)
            {
                // The opener's panel is this slot's own, so the toggle closes it and there is no click to send.
                Settle(id);
            }
        }
        else if (!passThrough)
        {
            DismissLater();
        }

        return contact.ReleaseOpens;
    }

    /// <summary>The pointer went up. A release still inside the slot toggles when the tap did not. The click's dismiss hold drops two turns later.</summary>
    /// <param name="id">Plugin id of the slot.</param>
    /// <param name="inside">True when the release is inside the slot.</param>
    /// <returns>Whether the capture should end, and whether this release is the plugin's left click.</returns>
    public SlotRelease PointerUp(string id, bool inside)
    {
        ArgumentNullException.ThrowIfNull(id);
        var contact = ContactFor(id);
        var click = false;
        var capture = contact.ReleaseOpens;
        if (contact.ReleaseOpens)
        {
            contact.ReleaseOpens = false;
            if (!contact.Press.Handled && inside)
            {
                click = Settle(id);
            }
        }

        EndContact(contact.Press);
        return new SlotRelease(capture, click);
    }

    /// <summary>The pointer was cancelled. The click does not toggle.</summary>
    /// <param name="id">Plugin id of the slot.</param>
    /// <returns>True when the slot captured the pointer and should let it go.</returns>
    public bool PointerCancel(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var contact = ContactFor(id);
        var capture = contact.ReleaseOpens;
        contact.ReleaseOpens = false;
        EndContact(contact.Press);
        return capture;
    }

    /// <summary>A tap. The first event of the click toggles. A later event of that click does not.</summary>
    /// <param name="id">Plugin id of the slot.</param>
    /// <returns>True when this tap is the plugin's left click.</returns>
    public bool Tap(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var press = ContactFor(id).Press;
        if (press.Down)
        {
            return Settle(id);
        }

        return !press.Handled && Invoke(id);
    }

    /// <summary>A keyboard invoke. It is one whole click, and its dismiss hold drops two turns later.</summary>
    /// <param name="id">Plugin id of the slot.</param>
    /// <returns>True when this invoke is the plugin's left click.</returns>
    public bool Invoke(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var press = ContactFor(id).Press;
        if (!press.Down)
        {
            press.Begin();
            Hold();
        }

        var click = Settle(id);
        EndContact(press);
        return click;
    }

    /// <summary>Forgets clicks. The bar calls this when it builds the slots again.</summary>
    public void ClearClicks()
    {
        // A click that is still down never ends on the new slots, and its dismiss hold would stay for good.
        foreach (var contact in _contacts.Values)
        {
            EndContact(contact.Press);
        }

        _contacts.Clear();
    }

    /// <summary>
    /// Notes a close and runs it on the next turn, unless a later show replaces it first.
    /// Nothing happens when the panel is not up or its window is still activating.
    /// </summary>
    private void DismissLater()
    {
        if (!_live || _accepting > 0)
        {
            return;
        }

        _live = false;
        var generation = _generation;
        _window.Post(() =>
        {
            if (generation == _generation)
            {
                Close();
            }
        });
    }

    /// <summary>True while a panel is up on another Bar than <paramref name="bar"/>, a close already noted included: until it has closed, a press there is still its dismissal.</summary>
    private bool Elsewhere(int bar) => _openId is not null && _bar != bar;

    /// <summary>True when <paramref name="open"/>, counted when a panel opened, is still the panel that is up. A refresh of it does not end it.</summary>
    private bool StillCurrent(int open) => open == _opens && _live;

    /// <summary>The contact ended. Its dismiss hold drops two turns later, so the deactivation from this click still sees the hold.</summary>
    private void EndContact(PanelPress press)
    {
        if (!press.Down)
        {
            return;
        }

        press.End();
        _window.Post(() => _window.Post(Unhold));
    }

    private void EndLeave(int leave)
    {
        // A slide a later show stopped does not end the leave that came after it.
        if (leave == _leaves)
        {
            _hiding = false;
        }
    }

    /// <summary>
    /// The first event of a click toggles. It closes <paramref name="id"/>'s panel when that is the one open.
    /// Otherwise it answers true: the plugin hears its left click and opens its panel, in place of any other.
    /// </summary>
    private bool Settle(string id)
    {
        var mine = string.Equals(_openId, id, StringComparison.Ordinal);
        var action = ContactFor(id).Press.Act(mine);
        if (action == PanelPressAction.Close)
        {
            Close();
        }

        return action == PanelPressAction.Open;
    }

    /// <summary>
    /// Keeps the panel up while an elevation prompt it asked for is showing.
    /// A deactivation and a press outside do not close it. A press on the bar still does.
    /// Call <paramref name="suspend"/> false on a later turn, after the prompt has returned.
    /// </summary>
    /// <param name="suspend">True to hold the panel open, false to release one hold.</param>
    public void SuspendDismiss(bool suspend)
    {
        if (suspend)
        {
            _suspend++;
        }
        else if (_suspend > 0)
        {
            _suspend--;
        }
    }

    /// <summary>Runs <paramref name="work"/> on a later turn of the UI thread.</summary>
    /// <param name="work">What to run.</param>
    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _window.Post(work);
    }

    /// <summary>Gives the panel the keyboard.</summary>
    public void TakeKeyboard() => _window.TakeKeyboard();

    /// <summary>A click is under way. A deactivation does not close the panel until <see cref="Unhold"/>.</summary>
    private void Hold() => _dismissHold++;

    private void Unhold()
    {
        if (_dismissHold > 0)
        {
            _dismissHold--;
        }
    }

    private Contact ContactFor(string id)
    {
        if (!_contacts.TryGetValue(id, out var contact))
        {
            contact = new Contact();
            _contacts[id] = contact;
        }

        return contact;
    }

    private sealed class Contact
    {
        public PanelPress Press { get; } = new();

        public bool ReleaseOpens { get; set; }
    }
}
