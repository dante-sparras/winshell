using System.Diagnostics;

namespace Winshell.TestSupport;

/// <summary>
/// The one way a test waits on time. An event, a task or <c>ManualEventLoop.Advance</c> is better when there is one.
/// This is for what gives no signal: a file, a window, another process. Every wait ends with the test, at its deadline.
/// </summary>
internal static class Wait
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FirstPause = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan LongestPause = TimeSpan.FromMilliseconds(100);

    /// <summary>Returns as soon as <paramref name="done"/> is true, and fails the test if it never is.</summary>
    /// <param name="done">What to wait for.</param>
    /// <param name="what">What the test was waiting for, in its failure message.</param>
    /// <param name="between">Runs between checks, usually to pump a loop.</param>
    internal static void Until(Func<bool> done, string what, Action? between = null)
    {
        var token = TestContext.Current.CancellationToken;
        var started = Stopwatch.GetTimestamp();
        var pause = FirstPause;
        while (!done())
        {
            Assert.True(Stopwatch.GetElapsedTime(started) < Patience, $"Timed out: {what}");
            token.WaitHandle.WaitOne(pause);
            token.ThrowIfCancellationRequested();
            pause = Next(pause);
            between?.Invoke();
        }
    }

    /// <summary><see cref="Until"/> for a test that must not block its thread, so what it awaits can run.</summary>
    internal static Task UntilAsync(Func<bool> done, string what, Action? between = null) =>
        UntilAsync(done, what, () =>
        {
            between?.Invoke();
            return Task.CompletedTask;
        });

    /// <summary><see cref="Until"/> with something to await between checks.</summary>
    internal static async Task UntilAsync(Func<bool> done, string what, Func<Task> between)
    {
        var token = TestContext.Current.CancellationToken;
        var started = Stopwatch.GetTimestamp();
        var pause = FirstPause;
        while (!done())
        {
            Assert.True(Stopwatch.GetElapsedTime(started) < Patience, $"Timed out: {what}");
#pragma warning disable RS0030 // This is the one place a test waits on time.
            await Task.Delay(pause, token);
#pragma warning restore RS0030
            pause = Next(pause);
            await between();
        }
    }

    /// <summary>
    /// Gives something that must not happen the time to happen. On a slow machine this can miss a bug, but it can't fail
    /// a test that should pass. Never use it to wait for something that should happen: that is <see cref="Until"/>.
    /// </summary>
    internal static void ForNothingToHappen(TimeSpan time)
    {
        var token = TestContext.Current.CancellationToken;
        token.WaitHandle.WaitOne(time);
        token.ThrowIfCancellationRequested();
    }

    private static TimeSpan Next(TimeSpan pause) => pause * 2 < LongestPause ? pause * 2 : LongestPause;
}
