using Winshell.Native;
using Winshell.Windows;

namespace Winshell.Services.Tests;

public sealed class WindowScanTests
{
    [Fact]
    public async Task A_hidden_window_on_a_thread_that_is_not_answering_does_not_stop_the_scan()
    {
        using var release = new ManualResetEventSlim();
        var made = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        // As SystemEvents is while it waits for the loop: it owns a hidden window, and it is not pumping.
        var owner = new Thread(() =>
        {
            var hwnd = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 1, 1, 0, 0, Win32.GetModuleHandleW(null), 0);
            made.SetResult(hwnd);
            release.Wait();
            Win32.DestroyWindow(hwnd);
        })
        { IsBackground = true };
        owner.Start();
        var window = await made.Task.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            var shows = await Task.Run(() => WindowBackend.IsTaskbarWindow(window), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.False(shows);
        }
        finally
        {
            release.Set();
        }
    }
}
