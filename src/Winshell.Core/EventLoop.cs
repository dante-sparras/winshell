namespace Winshell;

/// <summary>The thread the shell's objects live on. Work from other threads is posted to it.</summary>
public interface IEventLoop
{
    /// <summary>Runs the action on the loop's thread. Safe to call from any thread.</summary>
    void Post(Action action);

    /// <summary>Runs the action once after the delay, on the loop's thread. Dispose to cancel.</summary>
    IDisposable Schedule(TimeSpan delay, Action action);

    /// <summary>Time since the loop started, for timers and animations.</summary>
    TimeSpan Now { get; }
}
