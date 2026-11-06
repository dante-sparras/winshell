namespace Winshell.ShellTests;

[Trait("Category", "Slow")]
public sealed class DispatcherLoopTests
{
    [Fact]
    public async Task A_timer_whose_handle_was_dropped_still_fires()
    {
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await ShellFixture.On(shell =>
        {
            shell.Loop.Schedule(TimeSpan.FromMilliseconds(300), fired.SetResult);
            return true;
        });
        for (var i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
