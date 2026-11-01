using Microsoft.Win32.SafeHandles;
using Winshell.Native;

namespace Winshell.Platform.Windows.Tests;

public class WinEventLoopDisposeTests
{
    [Fact]
    public async Task A_post_after_dispose_signals_no_other_handle()
    {
        var loop = new WinEventLoop();
        loop.Dispose();
        // Windows reuses a closed handle's value, so this event can be the one the loop closed.
        var handle = Win32.CreateEventW(0, false, false, 0);
        using var other = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(handle, true) };

        await Task.Run(() => loop.Post(() => { }), TestContext.Current.CancellationToken);

        Assert.False(other.WaitOne(0));
    }

    [Fact]
    public void Dispose_twice_and_quit_after_dispose_do_not_throw()
    {
        var loop = new WinEventLoop();

        loop.Dispose();
        loop.Dispose();
        loop.Quit(0);
    }
}
