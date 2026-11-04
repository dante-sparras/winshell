namespace Winshell.Audio;

/// <summary>Runs an action once when disposed.</summary>
internal sealed class DisposeAction(Action action) : IDisposable
{
    private Action? _action = action;

    public void Dispose()
    {
        _action?.Invoke();
        _action = null;
    }
}
