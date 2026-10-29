using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A listening window does not rebuild its accessibility tree on every frame. An accessible edit and a notification
/// still update on the frame they happen, and a quiet window catches up on its own.
/// </summary>
public class UiaRefreshTests
{
    private const string Config = """
        import QtQuick
        import Winshell
        ShellRoot {
          PanelWindow {
            visible: false
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 80
            implicitHeight: 40
            Rectangle { objectName: "box"; width: 8; height: 8 }
            Item {
              objectName: "go"
              width: 8; height: 8
              Accessible.role: Accessible.Button
              Accessible.name: "Go"
            }
            Item {
              objectName: "notice"
              width: 8; height: 8
              visible: false
              Accessible.role: Accessible.Notification
              Accessible.name: "Volume 10 percent"
            }
          }
        }
        """;

    [Fact]
    public void Frames_do_not_rebuild_the_tree_and_an_edit_does()
    {
        WithWindow((_, automation, go, notice) =>
        {
            Assert.NotNull(automation.ElementFor(go));
            Assert.Equal(0, automation.SyncCount);
            automation.AfterFrame();
            automation.AfterFrame();
            automation.AfterFrame();
            Assert.Equal(1, automation.SyncCount);

            var accessible = go.GetAttached(o => new AccessibleAttached(o));
            accessible.Name = "Stop";
            automation.AfterFrame();
            Assert.Equal(2, automation.SyncCount);

            notice.Visible = true;
            automation.AfterFrame();
            Assert.Equal(3, automation.SyncCount);
            automation.AfterFrame();
            Assert.Equal(3, automation.SyncCount);

            var spoken = new List<string>();
            automation.Announce = (_, text) => spoken.Add(text);
            notice.Visible = false;
            automation.AfterFrame();
            notice.Visible = true;
            automation.AfterFrame();
            Assert.Equal(["Volume 10 percent"], spoken);
        });
    }

    [Fact]
    public void A_quiet_window_catches_up_without_another_frame()
    {
        WithWindow((loop, automation, go, _) =>
        {
            Assert.NotNull(automation.ElementFor(go));
            automation.AfterFrame();
            automation.AfterFrame();
            Assert.Equal(1, automation.SyncCount);
            loop.Schedule(TimeSpan.FromMilliseconds(180), () => loop.Quit(0));
            Assert.Equal(0, loop.Run());
            Assert.Equal(2, automation.SyncCount);
        });
    }

    [Fact]
    public void A_notification_is_announced_before_any_client_asks()
    {
        WithWindow((_, automation, _, notice) =>
        {
            var spoken = new List<string>();
            automation.Announce = (_, text) => spoken.Add(text);
            automation.AfterFrame();
            Assert.Equal(0, automation.SyncCount);
            notice.Visible = true;
            automation.AfterFrame();
            Assert.Equal(["Volume 10 percent"], spoken);
            Assert.Equal(1, automation.SyncCount);
            automation.AfterFrame();
            Assert.Equal(1, automation.SyncCount);
        });
    }

    private static void WithWindow(Action<WinEventLoop, Accessibility.WindowAutomation, Item, Item> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), Config);
        var log = new Log { Sink = (_, _, _) => { } };
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(Path.Combine(dir, "shell.qml"), loop, platform, log) { WatchFiles = false };
            Assert.True(host.Start());
            var native = Assert.Single(platform.Windows);
            var shell = (ShellWindow)native.Client;
            native.Automation.Listening = () => true;
            var go = Find(shell.ContentItem, "go");
            var notice = Find(shell.ContentItem, "notice");
            Assert.NotNull(go);
            Assert.NotNull(notice);
            body(loop, native.Automation, go, notice);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Item? Find(Item item, string name)
    {
        if (item.ObjectName == name)
            return item;
        foreach (var child in item.ChildItems)
        {
            var found = Find(child, name);
            if (found is not null)
                return found;
        }
        return null;
    }
}
