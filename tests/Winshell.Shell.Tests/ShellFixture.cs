using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Winshell.ShellTests;
using Xunit.v3;

[assembly: NoWindowLeftOpen]

namespace Winshell.ShellTests;

/// <summary>One shell for the whole test run, on a thread of its own. WinUI starts once in a process.</summary>
internal static class ShellFixture
{
    private static readonly TaskCompletionSource<Shell> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly Lazy<Thread> UiThread = new(StartThread);

    // One test uses the shell at a time. An action that awaits still owns its windows, and the shell is shared.
    private static readonly SemaphoreSlim OneAction = new(1, 1);
    private static readonly AsyncLocal<int> Depth = new();

    // The windows each test's turns on the shell left open, by name, for NoWindowLeftOpen to report once it ends.
    private static readonly ConcurrentDictionary<string, List<string>> Left = new();

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
    public static Task<T> On<T>(Func<Shell, Task<T>> action) => Gate(async () =>
        await OnThread(await Current().WaitAsync(TestContext.Current.CancellationToken), action));

    /// <summary>
    /// Keeps every other shell action out until <paramref name="action"/> finishes. A test that opens a window from the
    /// control pipe, rather than from <see cref="On"/>, holds this across that window's whole life.
    /// </summary>
    public static Task Hold(Func<Task> action) => Gate(async () =>
    {
        await action();
        return true;
    });

    private static async Task<T> Gate<T>(Func<Task<T>> body)
    {
        if (Depth.Value > 0)
            return await body();
        await OneAction.WaitAsync(TestContext.Current.CancellationToken);
        Depth.Value = 1;
        try
        {
            var shell = await Current().WaitAsync(TestContext.Current.CancellationToken);
            var before = await OnThread(shell, s => Task.FromResult(s.Platform.Windows.ToList()));
            try
            {
                return await body();
            }
            finally
            {
                await CloseLeft(shell, before);
            }
        }
        finally
        {
            Depth.Value = 0;
            OneAction.Release();
        }
    }

    public static Task<T> On<T>(Func<Shell, T> action) => On(shell => Task.FromResult(action(shell)));

    /// <summary>
    /// Closes the windows a turn opened and left open, before the next turn can see them, and keeps their names for
    /// <see cref="NoWindowLeftOpenAttribute"/>.
    /// </summary>
    private static async Task CloseLeft(Shell shell, List<HostWindow> before)
    {
        try
        {
            var left = await OnThread(shell, s =>
            {
                var opened = s.Platform.Windows.Except(before).ToList();
                var names = opened.Select(Describe).ToList();
                foreach (var window in opened)
                    (window.Client as ShellWindow)?.Dispose();
                return Task.FromResult(names);
            });
            if (left.Count > 0 && TestContext.Current.Test is { } test)
                Left.AddOrUpdate(test.UniqueID, left, (_, earlier) => [.. earlier, .. left]);
        }
        catch (OperationCanceledException)
        {
            // The test ran out of time, which already fails it.
        }
    }

    private static async Task<T> OnThread<T>(Shell shell, Func<Shell, Task<T>> action)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.Loop.Post(() => _ = Run(shell, action, done));
        return await done.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

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

    /// <summary>The names of the windows <paramref name="test"/>'s turns on the shell left open.</summary>
    internal static List<string> LeftOpenBy(IXunitTest test) => Left.TryRemove(test.UniqueID, out var names) ? names : [];

    /// <summary>A window as a failure names it: its type, its title or the name of what it shows, and what it shows.</summary>
    private static string Describe(HostWindow window)
    {
        var content = (window.Client as ShellWindow)?.Content;
        var name = window.Client is FloatingWindow floating ? floating.Title : content is null ? "" : AutomationProperties.GetName(content);
        return $"{window.Client.GetType().Name} \"{name}\" showing {content?.GetType().Name ?? "nothing"}";
    }

    private sealed class Configuration : ShellConfiguration
    {
        public override void Start(Shell shell) => Ready.SetResult(shell);
    }
}

/// <summary>
/// Fails a test whose turn on the shell left a window open, and names the window. The shell is one for the whole run, so
/// a window left open would fail the next test that counts the shell's windows. Each turn holds the shell to itself, and
/// the windows new when it ends are closed then, before another turn can see them.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class NoWindowLeftOpenAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        var left = ShellFixture.LeftOpenBy(test);
        Assert.True(left.Count == 0, $"The test left {left.Count} shell window(s) open: {string.Join("; ", left)}.");
    }
}
