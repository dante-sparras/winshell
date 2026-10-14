namespace Winshell;

/// <summary>
/// Keeps .NET's time zone in step with Windows. .NET reads the zone once and keeps it, and reading it again costs a registry walk,
/// so the kept zone is thrown away only when Windows names another one.
/// </summary>
internal sealed class ZoneWatch
{
    /// <summary>How long one answer about the zone is trusted. Under a minute, so a clock that ticks once a minute never shows a zone older than this.</summary>
    internal const long EveryMilliseconds = 30_000;

    private readonly Func<string> _zone;
    private readonly Action _forget;
    private string? _seen;
    private long _asked = long.MinValue;

    /// <summary>Watches the zone <paramref name="zone"/> reports.</summary>
    /// <param name="zone">Names the zone Windows is in now, with its offsets. It must be cheap, and must not read .NET's kept zone.</param>
    /// <param name="forget">Throws away the zone .NET keeps, so the next read of local time asks Windows.</param>
    internal ZoneWatch(Func<string> zone, Action forget)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(forget);
        _zone = zone;
        _forget = forget;
    }

    /// <summary>
    /// Asks for the zone when the last answer is older than <see cref="EveryMilliseconds"/>, and forgets the kept zone when it changed.
    /// The first call only remembers the zone, since .NET has just read the same one.
    /// </summary>
    /// <param name="now">Millisecond clock reading.</param>
    /// <returns>True when the zone changed, so every clock on screen is stale.</returns>
    internal bool Check(long now)
    {
        if (_asked != long.MinValue && now - _asked < EveryMilliseconds)
        {
            return false;
        }

        _asked = now;
        var zone = _zone();
        if (_seen is null)
        {
            _seen = zone;
            return false;
        }

        if (string.Equals(zone, _seen, StringComparison.Ordinal))
        {
            return false;
        }

        _seen = zone;
        _forget();
        return true;
    }
}
