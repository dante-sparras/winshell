using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A notification that stays on screen and changes its name, as the volume readout does, is spoken again.
/// </summary>
public class NotificationRenameTests
{
    private const string Config = """
        import Winshell
        import QtQuick
        ShellRoot {
          PanelWindow {
            anchors { top: true; left: true }
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 200
            implicitHeight: 60
            Rectangle {
              objectName: "notice"
              anchors.fill: parent
              visible: false
              Accessible.role: Accessible.Notification
              Accessible.name: "Volume 52 percent"
              Text { text: "Volume" }
            }
          }
        }
        """;

    [Fact]
    public void A_notification_that_changes_its_name_is_announced_again()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var root = Path.Combine(dir, "shell.qml");
        File.WriteAllText(root, Config);
        var log = new Log { Sink = (_, _, _) => { } };
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(root, loop, platform, log) { WatchFiles = false };
            Assert.True(host.Start());
            var backend = Assert.Single(platform.Windows);
            var window = (ShellWindow)backend.Client;
            var notice = All(window.Scene.Root).Single(i => i.ObjectName == "notice");
            var accessible = (AccessibleAttached)notice.FindAttached(typeof(AccessibleAttached))!;
            var spoken = new List<string>();
            backend.Automation.Listening = () => true;
            backend.Automation.Announce = (_, text) => spoken.Add(text);

            var steps = new Queue<Action>(
            [
                () => notice.WriteProperty("visible", true),
                () => accessible.Name = "Volume 56 percent",
                () => accessible.Name = "Volume 60 percent",
                () => accessible.Name = "",
            ]);
            var heard = new List<int>();
            void Next()
            {
                heard.Add(spoken.Count);
                if (!steps.TryDequeue(out var step))
                {
                    loop.Quit(0);
                    return;
                }
                step();
                loop.Schedule(TimeSpan.FromMilliseconds(80), Next);
            }
            loop.Schedule(TimeSpan.FromMilliseconds(80), Next);
            loop.Schedule(TimeSpan.FromSeconds(10), () => loop.Quit(1));
            Assert.Equal(0, loop.Run());

            Assert.Equal([0, 1, 2, 3, 3], heard);
            Assert.Equal(["Volume 52 percent", "Volume 56 percent", "Volume 60 percent"], spoken);
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

    private static IEnumerable<Item> All(Item item) => item.ChildItems.SelectMany(All).Prepend(item);
}
