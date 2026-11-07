namespace Winshell;

/// <summary>The thread the shell's objects live on, and their clock. Work from other threads is posted to it.</summary>
public interface IEventLoop
{
    /// <summary>Runs the action on the loop's thread. Safe to call from any thread.</summary>
    void Post(Action action);

    /// <summary>Runs the action once after the delay, on the loop's thread. Dispose to cancel.</summary>
    IDisposable Schedule(TimeSpan delay, Action action);

    /// <summary>
    /// The clock the shell's objects read. A timer it creates runs on another thread: use <see cref="Schedule"/> for
    /// work on the loop.
    /// </summary>
    TimeProvider Time { get; }
}
