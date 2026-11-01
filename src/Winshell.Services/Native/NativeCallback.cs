
namespace Winshell.Native;

/// <summary>
/// Runs the body of an <c>[UnmanagedCallersOnly]</c> callback. An exception that reaches Windows ends the process
/// with no log, so a failure is logged and the callback returns as if nothing had happened.
/// </summary>
#pragma warning disable CA1031 // Windows called in; nothing above this frame can catch it.
internal static class NativeCallback
{
    public static void Guard(Log log, string category, Action body)
    {
        try
        {
            body();
        }
        catch (Exception e)
        {
            Report(log, category, e);
        }
    }

    public static T Guard<T>(Log log, string category, Func<T> body, T fallback)
    {
        try
        {
            return body();
        }
        catch (Exception e)
        {
            Report(log, category, e);
            return fallback;
        }
    }

    private static void Report(Log log, string category, Exception e)
    {
        try
        {
            log.Warn(category, e.Message);
        }
        catch (Exception)
        {
            // The callback must return. A failure in the log sink cannot strand it.
        }
    }
}
#pragma warning restore CA1031
