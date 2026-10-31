using System.Diagnostics;
using Winshell.Platform.Windows.Services.Audio;
using Xunit;

namespace Winshell.Platform.Windows.Tests;

public class SessionExitTests
{
    [Fact]
    public void Stopping_the_watch_drops_the_exit_callback()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping -n 60 127.0.0.1 >nul",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        Assert.NotNull(process);
        var ran = 0;
        EventHandler handler = (_, _) => Interlocked.Increment(ref ran);
        process.EnableRaisingEvents = true;
        process.Exited += handler;

        SessionNode.StopWatching(process, handler);
        // End it here. On a busy runner ping -n 2 was still alive when the 8 second wait gave up, so the
        // callback check never ran. Killing is the exit the callback must ignore.
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.True(process.WaitForExit(8000));
        Thread.Sleep(200);
        Assert.Equal(0, Volatile.Read(ref ran));
    }
}
