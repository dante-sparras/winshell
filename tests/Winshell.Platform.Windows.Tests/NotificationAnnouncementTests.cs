using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A notification does not take focus, so a screen reader hears it only through a UI Automation notification event.
/// A real window whose notification shows announces its text once, even before any client asked for the window,
/// not again when it repaints, and again the next time it shows. A client listening is simulated: the test replaces
/// the window's check for listeners and records what would be spoken, so nothing is spoken on this PC.
/// </summary>
public class NotificationAnnouncementTests
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
              Accessible.name: "Your configuration has an error. shell.qml, line 4, column 5"
              Text { text: "Your configuration has an error" }
            }
          }
        }
        """;

    [Fact]
    public void A_notification_is_announced_once_each_time_it_shows()
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
            var spoken = new List<string>();
            backend.Automation.Listening = () => true;
            backend.Automation.Announce = (_, text) => spoken.Add(text);

            var steps = new Queue<Action>(
            [
                () => notice.WriteProperty("visible", true),
                window.Scene.RequestUpdate,
                window.Scene.RequestUpdate,
                () => notice.WriteProperty("visible", false),
                () => notice.WriteProperty("visible", true),
                window.Scene.RequestUpdate,
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

            // Nothing while hidden; once when shown; nothing on two repaints or while hidden; once when shown again.
            Assert.Equal([0, 1, 1, 1, 1, 2, 2], heard);
            Assert.All(spoken, s => Assert.Equal("Your configuration has an error. shell.qml, line 4, column 5", s));
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
