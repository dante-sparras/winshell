using Winshell.Windows;

namespace Winshell.Services.Tests;

/// <summary>Which screens another app is fullscreen on, over a fake backend, so no real window is read.</summary>
public sealed class FullscreenAppsTests
{
    private readonly ManualEventLoop _loop = new();
    private readonly FakeWindowBackend _windows = new();

    [Fact]
    public void Only_the_foreground_window_changes_which_screens_are_fullscreen()
    {
        _windows.Focus(1);
        using var apps = new FullscreenApps(_loop, Log.Default, _windows);
        var changes = 0;
        apps.Changed += () => changes++;

        _windows.Fullscreen.Add("M1");
        _windows.Move(2);
        _loop.Process();
        Assert.False(apps.On("M1"));

        _windows.Move(1);
        _loop.Process();
        Assert.True(apps.On("M1"));
        Assert.Equal(1, changes);

        _windows.Move(1);
        _loop.Process();
        Assert.Equal(1, changes);

        _windows.Fullscreen.Clear();
        _windows.Focus(2);
        _loop.Process();
        Assert.False(apps.On("M1"));
        Assert.Equal(2, changes);
    }
}
