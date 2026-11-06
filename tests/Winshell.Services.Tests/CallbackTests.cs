using Winshell.Native;

namespace Winshell.Services.Tests;

/// <summary>
/// A callback Windows makes hands its handler's exception to the reporter its caller gave, and Windows gets the
/// callback's usual answer. Shown through a message-only window, which nothing else in the session can find.
/// </summary>
public sealed class CallbackTests : IDisposable
{
    private const uint Probe = 0x0400 + 1;
    private readonly List<NativeWindow> _windows = [];

    [Fact]
    public void A_throwing_handler_is_logged_and_the_message_gets_the_default_answer()
    {
        var lines = new List<string>();
        var log = new Log { Sink = (level, category, message) => lines.Add($"{level} {category}: {message}") };
        var window = Open(e => log.Warn("tray", e.Message));

        Assert.Equal(0, Win32.SendMessageW(window.Handle, Probe, 0, 0));
        Assert.Equal(["Warn tray: broken"], lines);
    }

    [Fact]
    public void A_failing_reporter_does_not_reach_Windows()
    {
        var window = Open(_ => throw new IOException("disk full"));

        Assert.Equal(0, Win32.SendMessageW(window.Handle, Probe, 0, 0));
    }

    private NativeWindow Open(Action<Exception> failed)
    {
        var window = NativeWindow.TryCreate("WinshellTestCallback", 0, 0,
            message => message.Id == Probe ? throw new InvalidOperationException("broken") : null, failed, true);
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
