using System.Collections.Immutable;

namespace Winshell;

/// <summary>
/// The handlers a backend shared by several services calls. Each service adds its handler with its own failure
/// reporter, so a handler that throws is reported to the service that added it, and the others still run.
/// </summary>
internal sealed class Listeners<THandler>
{
    private ImmutableArray<Listener> _all = [];

    /// <summary>Adds a handler until the result is disposed. Any thread.</summary>
    /// <param name="handler">Called by <see cref="Raise"/>.</param>
    /// <param name="failed">Gets each exception <paramref name="handler"/> throws.</param>
    public IDisposable Add(THandler handler, Action<Exception> failed)
    {
        var listener = new Listener(this, handler, failed);
        ImmutableInterlocked.Update(ref _all, static (all, added) => all.Add(added), listener);
        return listener;
    }

    /// <summary>Calls each handler in turn, on this thread.</summary>
    public void Raise(Action<THandler> call)
    {
        foreach (var listener in _all)
            try
            {
                call(listener.Handler);
            }
            catch (Exception e)
            {
                listener.Failed(e);
            }
    }

    private sealed class Listener(Listeners<THandler> owner, THandler handler, Action<Exception> failed) : IDisposable
    {
        public THandler Handler => handler;

        public Action<Exception> Failed => failed;

        public void Dispose()
        {
            ImmutableInterlocked.Update(ref owner._all, static (all, removed) => all.Remove(removed), this);
        }
    }
}
