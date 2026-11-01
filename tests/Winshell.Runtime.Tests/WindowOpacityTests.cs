using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// <c>ShellWindow.opacity</c> and <c>ShellWindow.visibleMask</c>. The fake backend records what it was told, so a
/// default of 1 or null adds no call, and a visible mask never becomes the input region.
/// </summary>
public class WindowOpacityTests
{
    private const string ShellQml = """
        import Winshell
        import QtQuick
        ShellRoot {
          PanelWindow {
            id: panel
            objectName: "panel"
            implicitWidth: 200
            implicitHeight: 48
            opacity: 1
            mask: Region { x: 1; y: 2; width: 3; height: 4 }
            visibleMask: Region {
              x: 0; y: 0; width: 10; height: 10
              Region { x: 50; y: 5; width: 10; height: 20 }
            }
          }
          FloatingWindow {
            objectName: "floating"
            implicitWidth: 120
            implicitHeight: 80
            opacity: 0.4
          }
          PopupWindow {
            objectName: "popup"
            anchor.window: panel
            implicitWidth: 30
            implicitHeight: 30
            opacity: 0
          }
        }
        """;

    [Fact]
    public void Defaults_stay_unsent_and_out_of_range_opacity_clamps_for_every_window_kind()
    {
        using var shell = new Shell(ShellQml);
        var panel = shell.Window("panel");
        var floating = shell.Window("floating");
        var popup = shell.Window("popup");

        Assert.Equal(1, panel.Opacity);
        Assert.Equal(0, panel.OpacitySets);
        Assert.Equal(0.4, floating.Opacity);
        Assert.Equal(1, floating.OpacitySets);
        Assert.Equal(0, popup.Opacity);
        Assert.Equal(1, popup.OpacitySets);
        Assert.Null(floating.VisibleMask);
        Assert.Equal(0, floating.VisibleMaskSets);
        Assert.Null(popup.VisibleMask);
        Assert.Equal(0, popup.VisibleMaskSets);

        var window = (ShellWindow)panel.Client;
        window.Opacity = 3;
        Assert.Equal(1, window.Opacity);
        Assert.Equal(0, panel.OpacitySets);
        window.Opacity = -0.2;
        Assert.Equal(0, window.Opacity);
        Assert.Equal(0, panel.Opacity);
        window.Opacity = 0.25;
        Assert.Equal(0.25, panel.Opacity);
        window.Opacity = double.NaN;
        Assert.Equal(0.25, window.Opacity);
        Assert.Equal(0.25, panel.Opacity);
    }

    [Fact]
    public void A_visible_mask_is_the_region_bounds_and_never_the_input_region()
    {
        using var shell = new Shell(ShellQml);
        var panel = shell.Window("panel");
        var window = (ShellWindow)panel.Client;
        var input = panel.InputRegion;

        Assert.Equal([new QRect(1, 2, 3, 4)], input);
        // The two rectangles span x 0..60 and y 0..25. The clip is that one rectangle, not the input mask.
        Assert.Equal(new QRect(0, 0, 60, 25), panel.VisibleMask);

        window.VisibleMask!.Width = 80;
        Assert.Equal(new QRect(0, 0, 80, 25), panel.VisibleMask);
        Assert.Equal(input, panel.InputRegion);

        window.VisibleMask = new Region { Width = 0, Height = 0 };
        Assert.Equal(new QRect(0, 0, 0, 0), panel.VisibleMask);
        Assert.Equal(input, panel.InputRegion);

        window.VisibleMask = null;
        Assert.Null(panel.VisibleMask);
        Assert.Equal(input, panel.InputRegion);
        Assert.Equal([new QRect(1, 2, 3, 4)], panel.InputRegion);
    }

    [Fact]
    public void Opacity_and_visibleMask_are_applied_again_when_the_native_window_is_replaced()
    {
        using var shell = new Shell(ShellQml);
        var first = shell.Window("floating");
        var window = (ShellWindow)first.Client;
        window.Opacity = 0.3;
        window.VisibleMask = new Region { X = 4, Y = 6, Width = 12, Height = 8 };
        var input = first.InputRegion;

        first.Client.OnFailed(new InvalidOperationException("the surface broke"));
        Assert.True(first.Disposed);
        shell.Files.Loop.Advance(TimeSpan.FromMilliseconds(900));
        Assert.DoesNotContain(shell.Platform.Windows, w => ReferenceEquals(w.Client, window));

        shell.Files.Loop.Advance(TimeSpan.FromMilliseconds(200));
        var again = Assert.Single(shell.Platform.Windows, w => ReferenceEquals(w.Client, window));
        Assert.NotSame(first, again);
        Assert.Equal(0.3, again.Opacity);
        Assert.Equal(new QRect(4, 6, 12, 8), again.VisibleMask);
        Assert.Equal(input, again.InputRegion);
        Assert.Null(again.InputRegion);
    }

    private sealed class Shell : IDisposable
    {
        public Shell(string qml)
        {
            Files = new TestShell(("shell.qml", qml));
            Host = new ShellHost(Path.Combine(Files.Directory, "shell.qml"), Files.Loop, Platform, Files.Log);
        }

        public TestShell Files { get; }

        public FakePlatform Platform { get; } = new();

        public ShellHost Host { get; }

        public FakeWindow Window(string name)
        {
            Start();
            return Platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == name);
        }

        public void Dispose()
        {
            Host.Dispose();
            Files.Dispose();
        }

        private bool _started;

        private void Start()
        {
            if (_started)
                return;
            _started = true;
            Assert.True(Host.Start(), string.Join("\n", Files.Messages));
            Files.Loop.Process();
        }
    }
}
