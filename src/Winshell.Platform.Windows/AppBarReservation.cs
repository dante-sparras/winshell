using Winshell.Platform.Windows.Native;

namespace Winshell.Platform.Windows;

/// <summary>
/// The rectangle a panel's app bar holds, and when to ask Explorer for another. Explorer announces a new work area
/// after a reservation moves, which places every panel again, so asking for what is already held would never end.
/// Explorer may also grant less than was asked, as when another app bar takes the space in between. That short answer
/// stands for the same request, because each ABM_SETPOS announces itself back, until something outside this bar
/// changes the work area. The same request is then asked for once more.
/// </summary>
internal sealed class AppBarReservation
{
    private RECT _lastRequest;
    private bool _requested;
    private bool _shortGrantStale;
    private bool _reserving;
    private bool _externalDuringReserve;

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
        _reserving = true;
        RECT granted;
        try
        {
            granted = setPos(desired);
        }
        finally
        {
            _reserving = false;
        }
        var moved = !granted.Equals(Granted);
        Granted = granted;
        // A notice that arrived inside ABM_SETPOS can only be judged once the grant is known.
        if (_externalDuringReserve && !Granted.Equals(_lastRequest))
            _shortGrantStale = true;
        _externalDuringReserve = false;
        return moved;
    }

    /// <summary>
    /// Another bar, or the work area, changed for a reason other than our own last request. A short grant may no
    /// longer be what Explorer would give, so the next request is asked for even when it is the same rectangle.
    /// </summary>
    public void NoteExternalChange()
    {
        if (_reserving)
        {
            _externalDuringReserve = true;
            return;
        }
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
        _externalDuringReserve = false;
    }
}

/// <summary>
/// Which <c>ABN_POSCHANGED</c> notices came from this bar's own <c>ABM_SETPOS</c>. Explorer sends one back when the
/// grant moves, and also one when some other bar moves. Only the other bar's notice makes a short grant worth asking
/// for again. A grant that did not move sends nothing, so that expectation is dropped and cannot swallow a later notice.
/// </summary>
internal sealed class AppBarPosNotices
{
    private int _expected;
    private int _seen;

    /// <summary>A notice since the last <see cref="TakeExternal"/> came from outside this bar.</summary>
    public bool External { get; private set; }

    /// <summary>Call immediately before <c>ABM_SETPOS</c>.</summary>
    public void ExpectOwn() => _expected++;

    /// <summary><c>ABM_SETPOS</c> returned. <paramref name="moved"/> is whether the granted rectangle changed.</summary>
    public void SetPosReturned(bool moved)
    {
        if (!moved && _seen < _expected)
            _expected--;
    }

    /// <summary>One <c>ABN_POSCHANGED</c> arrived.</summary>
    public void Delivered()
    {
        if (_seen < _expected)
            _seen++;
        else
            External = true;
    }

    /// <summary>Whether an outside notice is waiting, then clears that fact.</summary>
    public bool TakeExternal()
    {
        var value = External;
        External = false;
        return value;
    }

    /// <summary>The app bar was removed.</summary>
    public void Reset()
    {
        _expected = 0;
        _seen = 0;
        External = false;
    }
}
