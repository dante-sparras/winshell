namespace Winshell;

/// <summary>
/// One action at a time on a device or service. A call while one runs does nothing, and the result reaches the
/// loop through <c>post</c>.
/// </summary>
internal sealed class DeviceAction(Action<Action> post)
{
    private int _running;

    public bool Running => Volatile.Read(ref _running) != 0;

    /// <summary>
    /// Calls <paramref name="start"/>, then <paramref name="work"/>, unless an action is running. When
    /// <c>post</c> runs its callback, <see cref="Running"/> ends and <paramref name="done"/> gets the error: what the
    /// work returned, or the text of an expected Windows failure. It is empty after success, and after any other
    /// exception, which still faults the task this returns.
    /// </summary>
    public async Task RunAsync(Action start, Func<Task<string?>> work, Action<string> done)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
            return;
        var error = "";
        try
        {
            start();
            error = await work().ConfigureAwait(false) ?? "";
        }
        catch (Exception e) when (WindowsFailure.IsExpected(e))
        {
            error = e.Message;
        }
        finally
        {
            post(() =>
            {
                Volatile.Write(ref _running, 0);
                done(error);
            });
        }
    }
}
