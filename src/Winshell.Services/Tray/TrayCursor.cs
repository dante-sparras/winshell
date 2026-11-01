
namespace Winshell.Tray;

/// <summary>Temporarily places the pointer for an old tray app without undoing the user's next mouse movement.</summary>
internal sealed class TrayCursor(IEventLoop loop, Func<(int X, int Y)> position, Action<(int X, int Y)> move) : IDisposable
{
    private IDisposable? _restore;

    public void Place((int X, int Y) target)
    {
        var previous = position();
        _restore?.Dispose();
        _restore = null;
        if (Math.Abs((long)target.X - previous.X) <= 3 && Math.Abs((long)target.Y - previous.Y) <= 3)
            return;

        move(target);
        _restore = loop.Schedule(TimeSpan.FromMilliseconds(400), () =>
        {
            _restore = null;
            if (position() == target)
                move(previous);
        });
    }

    public void Dispose() => _restore?.Dispose();
}
