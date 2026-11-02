using Microsoft.UI.Xaml;

namespace Winshell.ShellTests;

/// <summary>One shell for the whole test run, on a thread of its own. WinUI starts once in a process.</summary>
internal static class ShellFixture
{
    private static readonly TaskCompletionSource<Shell> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly Lazy<Thread> UiThread = new(StartThread);

    private static Thread StartThread()
    {
        // This process becomes an instance, and it must not join the user's own.
        Environment.SetEnvironmentVariable("WINSHELL_LOCAL_DIR", Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N")));
        var thread = new Thread(() => Shell.Run<Configuration>(new ShellOptions { ShellId = "winshell-tests" }))
        {
            IsBackground = true,
            Name = "WinUI",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    public static Task<Shell> Current()
    {
        _ = UiThread.Value;
        return Ready.Task;
    }

    /// <summary>Runs <paramref name="action"/> on the shell's thread and returns what it returned.</summary>
    public static async Task<T> On<T>(Func<Shell, Task<T>> action)
    {
        var shell = await Current();
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.Loop.Post(() => _ = Run(shell, action, done));
        return await done.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    public static Task<T> On<T>(Func<Shell, T> action) => On(shell => Task.FromResult(action(shell)));

    private static async Task Run<T>(Shell shell, Func<Shell, Task<T>> action, TaskCompletionSource<T> done)
    {
        try
        {
            done.SetResult(await action(shell));
        }
#pragma warning disable CA1031 // It fails the test that asked.
        catch (Exception e)
#pragma warning restore CA1031
        {
            done.SetException(e);
        }
    }

    /// <summary>Completes once the element has a size, which comes after it loads.</summary>
    public static Task LaidOut(FrameworkElement element)
    {
        if (element.ActualWidth > 0)
            return Task.CompletedTask;
        var sized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        element.SizeChanged += (_, _) => sized.TrySetResult();
        return sized.Task;
    }

    private sealed class Configuration : ShellConfiguration
    {
        public override void Start(Shell shell) => Ready.SetResult(shell);
    }
}
