namespace Winshell;

/// <summary>Runs an action the first time it is disposed, from any thread. Later calls do nothing.</summary>
internal sealed class DisposeAction(Action action) : IDisposable
{
    private Action? _action = action;

    public void Dispose()
    {
        Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}
