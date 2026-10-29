using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

public class MouseHookThreadTests
{
    private const string Popup = """
        import Winshell
        ShellRoot {
          PanelWindow {
            id: bar
            anchors { top: true; left: true }
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 120
            implicitHeight: 40
          }
          PopupWindow {
            anchor.window: bar
            grabFocus: true
            visible: false
            implicitWidth: 120
            implicitHeight: 40
          }
        }
        """;

    private const string Grab = """
        import Winshell
        ShellRoot {
          PanelWindow {
            id: bar
            id: bar
            anchors { top: true; left: true }
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 120
            implicitHeight: 40
          }
          FocusGrab { windows: [bar] }
        }
        """;

    [Fact]
    public void The_popup_mouse_hook_runs_on_its_own_thread_until_the_popup_hides()
    {
        var (hooked, after) = Run(Popup, platform => platform.MouseHookThread,
            host => host.Root!.OwnedObjects.OfType<PopupWindow>().Single().Visible = true,
            host => host.Root!.OwnedObjects.OfType<PopupWindow>().Single().Visible = false);

        Assert.NotNull(hooked);
        Assert.NotEqual(Environment.CurrentManagedThreadId, hooked.ManagedThreadId);
        Assert.False(hooked.IsAlive);
        Assert.Null(after);
    }

    [Fact]
    public void The_focus_grab_mouse_hook_runs_on_its_own_thread_until_the_grab_ends()
    {
        var (hooked, after) = Run(Grab.Replace("PanelWindow {", "PanelWindow {\n    id: bar"), _ => FocusGrab.InstalledHookThread,
            host => host.Root!.OwnedObjects.OfType<FocusGrab>().Single().Active = true,
            host => host.Root!.OwnedObjects.OfType<FocusGrab>().Single().Active = false);

        Assert.NotNull(hooked);
        Assert.NotEqual(Environment.CurrentManagedThreadId, hooked.ManagedThreadId);
        Assert.False(hooked.IsAlive);
        Assert.Null(after);
    }

    // Runs a shell. Reads the hook thread while the window is up, hides it, then reads the hook thread again.
    private static (Thread? Hooked, Thread? After) Run(string qml, Func<WindowsPlatform, Thread?> hook, Action<ShellHost> show, Action<ShellHost> hide)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "shell.qml");
        File.WriteAllText(path, qml);
        try
        {
            var log = new Log();
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(path, loop, platform, log, () =>
            {
                var modules = StandardModules.Create();
                Services.WindowManagerModules.Register(modules);
                return modules;
            }) { WatchFiles = false };
            Assert.True(host.Start());
            Thread? hooked = null;
            Thread? after = null;
            loop.Schedule(TimeSpan.FromMilliseconds(100), () => show(host));
            loop.Schedule(TimeSpan.FromMilliseconds(400), () =>
            {
                hooked = hook(platform);
                hide(host);
            });
            loop.Schedule(TimeSpan.FromMilliseconds(600), () =>
            {
                after = hook(platform);
                loop.Quit(0);
            });
            loop.Run();
            return (hooked, after);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
