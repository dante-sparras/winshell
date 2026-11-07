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
    /// <param name="handler">Called by <see cref="Raise{TArg}"/>.</param>
    /// <param name="failed">Gets each exception <paramref name="handler"/> throws.</param>
    public IDisposable Add(THandler handler, Action<Exception> failed)
    {
        var listener = new Listener(this, handler, failed);
        ImmutableInterlocked.Update(ref _all, static (all, added) => all.Add(added), listener);
        return listener;
    }

    /// <summary>Calls each handler in turn, on this thread, with <paramref name="arg"/>. It never throws.</summary>
    public void Raise<TArg>(TArg arg, Action<THandler, TArg> call)
    {
        foreach (var listener in _all)
            try
            {
                call(listener.Handler, arg);
            }
            catch (Exception e)
            {
                try
                {
                    listener.Failed(e);
                }
                catch (Exception)
                {
                    // A reporter that throws must not stop the other handlers, or the thread that raises them.
                }
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

/// <summary>What a Windows callback that only raises <see cref="Listeners{THandler}"/> needs.</summary>
internal static class Listeners
{
    /// <summary>
    /// The failure reporter for such a callback. <see cref="Listeners{THandler}.Raise{TArg}"/> hands every failure to
    /// the service whose handler failed and never throws, so nothing reaches it.
    /// </summary>
    public static readonly Action<Exception> RaiseNeverFails = static _ => { };
}
