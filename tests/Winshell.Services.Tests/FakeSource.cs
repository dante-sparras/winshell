namespace Winshell.Services.Tests;

/// <summary>A snapshot source a test feeds by hand.</summary>
internal sealed class FakeSource<T>(T initial) : SnapshotSource<T>(initial)
    where T : class
{
    protected override string Category => "test";

    public void Push(T next)
    {
        Publish(next);
    }

    protected override Task StartAsync()
    {
        return Task.CompletedTask;
    }

    protected override Task<T> ReadAsync()
    {
        return Task.FromResult(Current);
    }
}
