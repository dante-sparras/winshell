using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// The body of every <c>[UnmanagedCallersOnly]</c> callback. An exception that reaches Windows ends the process with no
/// log, so none does. A callback Windows makes on its own schedule hands its failure to the reporter its caller gave,
/// and returns as if nothing had happened. A callback inside an enumeration stops it, and the enumeration throws the
/// failure to its caller once Windows has returned.
/// </summary>
#pragma warning disable CA1031 // Windows called in; nothing above this frame can catch it.
internal static class Callback
{
    internal static void Run(Action<Exception> failed, Action body)
    {
        try
        {
            body();
        }
        catch (Exception e)
        {
            Report(failed, e);
        }
    }

    internal static T Run<T>(Action<Exception> failed, Func<T> body, T fallback)
    {
        try
        {
            return body();
        }
        catch (Exception e)
        {
            Report(failed, e);
            return fallback;
        }
    }

    private static void Report(Action<Exception> failed, Exception e)
    {
        try
        {
            failed(e);
        }
        catch (Exception)
        {
            // The callback must return. A failing reporter cannot strand it.
        }
    }

    /// <summary>
    /// The handles an enumeration passes to its callback, in order. <paramref name="enumerate"/> starts it with the
    /// context its callback passes to <see cref="Collected"/>.
    /// </summary>
    internal static List<nint> Collect(Action<nint> enumerate)
    {
        var collection = new Collection();
        var handle = GCHandle.Alloc(collection);
        try
        {
            enumerate(GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        collection.Failure?.Throw();
        return collection.Handles;
    }

    /// <summary>The body of an enumeration callback. Returns 1 to go on, 0 to stop.</summary>
    internal static int Collected(nint context, nint handle)
    {
        var collection = (Collection)GCHandle.FromIntPtr(context).Target!;
        try
        {
            collection.Handles.Add(handle);
            return 1;
        }
        catch (Exception e)
        {
            collection.Failure = ExceptionDispatchInfo.Capture(e);
            return 0;
        }
    }

    private sealed class Collection
    {
        public List<nint> Handles { get; } = [];

        public ExceptionDispatchInfo? Failure { get; set; }
    }
}
#pragma warning restore CA1031
