using Winshell.Native;

namespace Winshell.Services.Tests;

/// <summary>
/// A callback Windows makes hands its handler's exception to the reporter its caller gave, and Windows gets the
/// callback's default answer. Shown through a message-only window, which nothing else in the session can find.
/// </summary>
public sealed class CallbackTests : IDisposable
{
    // WM_QUERYOPEN: DefWindowProc answers it with TRUE, so the default answer is not 0.
    private const uint Probe = 0x0013;
    private readonly List<NativeWindow> _windows = [];

    [Fact]
    public void A_throwing_handler_reaches_the_reporter_and_Windows_gets_the_default_answer()
    {
        var thrown = new InvalidOperationException("broken");
        var reported = new List<Exception>();
        var window = Open(thrown, reported.Add);

        Assert.Equal(1, Win32.SendMessageW(window.Handle, Probe, 0, 0));
        Assert.Same(thrown, Assert.Single(reported));
    }

    [Fact]
    public void A_failing_reporter_does_not_reach_Windows()
    {
        var window = Open(new InvalidOperationException("broken"), _ => throw new IOException("disk full"));

        Assert.Equal(1, Win32.SendMessageW(window.Handle, Probe, 0, 0));
    }

    private NativeWindow Open(Exception thrown, Action<Exception> failed)
    {
        var window = NativeWindow.TryCreate("WinshellTestCallback", 0, 0,
            message => message.Id == Probe ? throw thrown : null, failed, true);
        Assert.NotNull(window);
        _windows.Add(window);
        return window;
    }

    public void Dispose()
    {
        foreach (var window in _windows)
            window.Release(true);
    }
}
