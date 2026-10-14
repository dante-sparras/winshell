namespace Winshell.Core;

/// <summary>What one event of a click on a panel icon should do.</summary>
internal enum PanelPressAction
{
    /// <summary>This event is not the first of its click.</summary>
    None,

    /// <summary>The panel is closed and this click opens it.</summary>
    Open,

    /// <summary>The panel is open and this click closes it.</summary>
    Close,
}

/// <summary>
/// One click on a panel icon toggles that panel once.
/// A later event from the same click does not toggle again, and the next click toggles the other way.
/// </summary>
internal sealed class PanelPress
{
    private int _press;
    private int _settled;
    private bool _down;

    /// <summary>True while the contact that started this click has not ended.</summary>
    public bool Down => _down;

    /// <summary>True after this click has already opened or closed the panel.</summary>
    public bool Handled => _press > 0 && _settled == _press;

    /// <summary>Starts a click. A repeat down for the same contact does nothing.</summary>
    public void Begin()
    {
        if (_down)
        {
            return;
        }

        _down = true;
        _press++;
    }

    /// <summary>This click does nothing more: it was used up by something else, such as dismissing a panel on another monitor.</summary>
    public void Spend() => _settled = _press;

    /// <summary>The contact ended.</summary>
    public void End() => _down = false;

    /// <summary>The first event of the current click toggles. Later events of that click do not.</summary>
    /// <param name="open">True when this icon's panel is open now.</param>
    /// <returns>Whether this event opens, closes, or changes nothing.</returns>
    public PanelPressAction Act(bool open)
    {
        if (_press == 0 || Handled)
        {
            return PanelPressAction.None;
        }

        _settled = _press;
        return open ? PanelPressAction.Close : PanelPressAction.Open;
    }
}
