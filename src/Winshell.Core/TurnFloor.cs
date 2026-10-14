namespace Winshell.Core;

/// <summary>
/// Spaces the turns a burst of window events asks for. A lone event runs its turn at once.
/// One that comes within <see cref="FloorMs"/> of the last turn waits out the rest of that time,
/// and every event that comes while it waits shares its turn.
/// </summary>
public sealed class TurnFloor
{
    /// <summary>The shortest time between the end of one turn and the start of the next, in milliseconds.</summary>
    public const long FloorMs = 40;

    private bool _ran;
    private long _at;

    /// <summary>How long a turn asked for at <paramref name="now"/> waits.</summary>
    /// <param name="now">Tick count in milliseconds.</param>
    /// <returns>Zero when the turn runs at once. Otherwise milliseconds, at most <see cref="FloorMs"/>.</returns>
    public long Wait(long now) => _ran ? Math.Clamp(FloorMs - (now - _at), 0, FloorMs) : 0;

    /// <summary>A turn ended at <paramref name="now"/>.</summary>
    /// <param name="now">Tick count in milliseconds.</param>
    public void Ran(long now)
    {
        _ran = true;
        _at = now;
    }
}
