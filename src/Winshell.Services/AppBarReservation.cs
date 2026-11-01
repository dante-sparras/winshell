using Winshell.Native;

namespace Winshell;

/// <summary>
/// The rectangle a panel's app bar holds, and when to ask Explorer for another. A reservation that moves the work
/// area places every panel again, so asking for what is already held would never end. Explorer may grant less than
/// was asked, as when another app bar takes the space in between. That short answer stands until an
/// <c>ABN_POSCHANGED</c> arrives. Every notice is a possible outside change: Explorer does not send one per own
/// request, and a same-rectangle <c>ABM_SETPOS</c> sends none. The same request is then asked for once more, and
/// that repeat cannot loop.
/// </summary>
internal sealed class AppBarReservation
{
    private RECT _lastRequest;
    private bool _requested;
    private bool _shortGrantStale;

    /// <summary>What Explorer granted last, or empty before the first request.</summary>
    public RECT Granted { get; private set; }

    /// <summary>
    /// Asks for <paramref name="desired"/> with <paramref name="setPos"/> (ABM_SETPOS, which returns the grant),
    /// unless that is what is held, or what was asked for last time and no outside change has made that short answer
    /// stale. Returns whether the grant moved, and with it the work area.
    /// </summary>
    public bool Reserve(RECT desired, Func<RECT, RECT> setPos)
    {
        var repeated = _requested && desired.Equals(_lastRequest);
        var retry = repeated && _shortGrantStale;
        _lastRequest = desired;
        _requested = true;
        _shortGrantStale = false;
        if ((desired.Equals(Granted) || repeated) && !retry)
            return false;
        var granted = setPos(desired);
        var moved = !granted.Equals(Granted);
        Granted = granted;
        return moved;
    }

    /// <summary>
    /// An <c>ABN_POSCHANGED</c> arrived. Explorer does not say which bar caused it, so each one may mean a short
    /// grant is no longer what the next request would get. A grant that already matches the last request is ignored.
    /// </summary>
    public void NoteExternalChange()
    {
        if (_requested && !Granted.Equals(_lastRequest))
            _shortGrantStale = true;
    }

    /// <summary>The app bar was removed: the next request is asked for.</summary>
    public void Clear()
    {
        Granted = default;
        _lastRequest = default;
        _requested = false;
        _shortGrantStale = false;
    }
}
