namespace Winshell.ShellTests;

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

    [Fact]
    public async Task A_disposed_timer_does_not_fire()
    {
        var count = 0;

        var ran = await ShellFixture.On(async shell =>
        {
            var timer = shell.Loop.Schedule(TimeSpan.FromMilliseconds(50), () => count++);
            timer.Dispose();
            var later = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            shell.Loop.Schedule(TimeSpan.FromMilliseconds(200), later.SetResult);
            await later.Task;
            return count;
        });

        Assert.Equal(0, ran);
    }
}
