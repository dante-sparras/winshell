using Winshell.Platform.Windows.Services.Unavailable;
using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Tests;

public class UnavailableServiceTests
{
    [Fact]
    public void Linux_only_modules_load_and_stay_idle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "shell.qml"), """
            import QtQuick
            import Quickshell
            import Quickshell.Services.Greetd
            import Quickshell.Services.Polkit
            import Quickshell.I3
            import Quickshell.DBusMenu
            import Quickshell.WindowManager
            import Quickshell.Wayland

            Item {
              property bool greetdUp: Greetd.available
              property int greetdState: Greetd.state
              property string greetdUser: Greetd.user
              property int greetdInactive: GreetdState.Inactive
              property string i3Socket: I3.socketPath
              property int i3Monitors: I3.monitors.values.length
              property int i3Workspaces: I3.workspaces.values.length
              property int windowsets: WindowManager.windowsets.length
              property int projections: WindowManager.windowsetProjections.length
              property int projectedScreens: WindowManager.screenProjection(null).screens.length
              PolkitAgent { id: agent }
              I3IpcListener { id: listener; subscriptions: ["window"] }
              ScreencopyView { id: copy; live: true; paintCursor: true }
              ShortcutInhibitor { id: keys; enabled: true }
              property bool agentRegistered: agent.isRegistered
              property bool agentActive: agent.isActive
              property var agentFlow: agent.flow
              property int listenerSubs: listener.subscriptions.length
              property bool copying: copy.hasContent
              property bool copyLive: copy.live
              property bool keysActive: keys.active
              Component.onCompleted: {
                Greetd.createSession("user")
                Greetd.respond("no")
                Greetd.launch(["cmd"])
                Greetd.cancelSession()
                I3.dispatch("nop")
                I3.refreshMonitors()
                copy.captureFrame()
              }
            }
            """);
        var messages = new List<string>();
        var log = new Log { Sink = (level, category, message) => messages.Add($"{level} {category}: {message}") };
        var registry = StandardModules.Create();
        Winshell.Platform.Windows.ServiceModules.Register(registry);
        Winshell.Platform.Windows.WaylandCompat.Register(registry);
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(registry, loop, log, directory);
        try
        {
            var root = engine.CreateRoot(Path.Combine(directory, "shell.qml"));
            loop.Process();

            Assert.False((bool)root.ReadProperty("greetdUp")!);
            Assert.Equal(0, root.ReadProperty("greetdState"));
            Assert.Equal(0, root.ReadProperty("greetdInactive"));
            Assert.Equal("", root.ReadProperty("greetdUser"));
            Assert.Equal("", root.ReadProperty("i3Socket"));
            Assert.Equal(0, root.ReadProperty("i3Monitors"));
            Assert.Equal(0, root.ReadProperty("i3Workspaces"));
            Assert.Equal(0, root.ReadProperty("windowsets"));
            Assert.Equal(0, root.ReadProperty("projections"));
            Assert.Equal(0, root.ReadProperty("projectedScreens"));
            Assert.False((bool)root.ReadProperty("agentRegistered")!);
            Assert.False((bool)root.ReadProperty("agentActive")!);
            Assert.Null(root.ReadProperty("agentFlow"));
            Assert.Equal(1, root.ReadProperty("listenerSubs"));
            Assert.False((bool)root.ReadProperty("copying")!);
            Assert.True((bool)root.ReadProperty("copyLive")!);
            Assert.False((bool)root.ReadProperty("keysActive")!);
            Assert.Contains(messages, message => message.Contains("no greetd socket", StringComparison.Ordinal));
            Assert.Contains(messages, message => message.Contains("no i3 socket", StringComparison.Ordinal));
            Assert.Contains(messages, message => message.Contains("window sets", StringComparison.Ordinal));
            Assert.Contains(messages, message => message.Contains("polkit agent", StringComparison.Ordinal));
            Assert.Contains(messages, message => message.Contains("Screencopy needs", StringComparison.Ordinal));
            Assert.Contains(messages, message => message.Contains("ShortcutInhibitor stays inactive", StringComparison.Ordinal));
            Assert.DoesNotContain(messages, message => message.Contains("ReferenceError", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void A_screen_projection_is_cached_per_screen()
    {
        var messages = new List<string>();
        var log = new Log { Sink = (_, _, message) => messages.Add(message) };
        using var engine = new QmlEngine(StandardModules.Create(), new ManualEventLoop(), log, Path.GetTempPath());
        var manager = new WindowManagerService(engine);
        var screen = new ShellScreen(new ScreenInfo(
            "1", "DISPLAY1", "Monitor", "Acme", "S1",
            new QRect(0, 0, 800, 600), new QRect(0, 0, 800, 600),
            1, 500, 300, 60, true));

        var first = manager.ProjectionFor(screen);
        Assert.Same(first, manager.ProjectionFor(screen));
        Assert.Same(screen, Assert.Single(first.Screens));
        Assert.Empty(first.Windowsets);
        Assert.NotSame(manager.ProjectionFor(null), manager.ProjectionFor(null));
        Assert.Empty(manager.ProjectionFor(null).Screens);
        Assert.Empty(manager.Windowsets);
        Assert.Contains(messages, message => message.Contains("window sets", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dbus_menu_has_no_layout_to_refresh()
    {
        var messages = new List<string>();
        var log = new Log { Sink = (_, category, message) => messages.Add($"{category}: {message}") };
        using var engine = new QmlEngine(StandardModules.Create(), new ManualEventLoop(), log, Path.GetTempPath());
        var item = new DBusMenuItem { Engine = engine };
        var handle = new DBusMenuHandle { Engine = engine };

        Assert.Null(item.MenuHandle);
        Assert.Null(handle.Menu);
        item.UpdateLayout();

        Assert.Contains(messages, message => message.Contains("no layout", StringComparison.Ordinal));
    }

    [Fact]
    public void An_auth_flow_never_starts()
    {
        var flow = new AuthFlow();
        Assert.Empty(flow.Identities);
        Assert.False(flow.Failed);
        Assert.False(flow.IsCompleted);
        flow.Submit("response");
        flow.CancelAuthenticationRequest();
        Assert.False(flow.IsDestroyed);
        ((IRetainable)flow).FinishRetain();
        Assert.True(flow.IsDestroyed);
    }
}
