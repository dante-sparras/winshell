using System.Runtime.CompilerServices;
using Winshell.Runtime.Quickshell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// A reload must release the generation it replaces. What outlives a generation (the host's settings and screens,
/// the theme, services) must not keep references to it, or every save of a config would grow the shell.
/// </summary>
public class ReloadLeakTests
{
    private const string Config = """
        import Quickshell
        import QtQuick
        import QtQuick.Controls
        import QtQuick.Layouts
        import "lib.js" as Lib

        ShellRoot {
          id: root
          property int reloads: 0

          Connections {
            target: Quickshell
            function onReloadCompleted() { root.reloads++ }
          }

          Variants {
            model: Quickshell.screens

            PanelWindow {
              required property var modelData
              screen: modelData
              anchors { top: true; left: true; right: true }
              implicitHeight: 40
              // A screen outlives a generation; a function connected to it must not keep this one alive.
              Component.onCompleted: modelData.widthChanged.connect(() => implicitHeight = 40)

              RowLayout {
                anchors.fill: parent
                Text { text: Lib.greet(Quickshell.shellDir); Layout.preferredWidth: modelData.width / 10 }
                Button { text: "button"; ToolTip.text: Quickshell.shellDir }
                Slider { value: 0.5 }
                TextField { text: "field" }
                Repeater { model: 5; Rectangle { required property int index; width: 10 + index; height: 10 } }
                Loader { sourceComponent: Text { text: "loaded" } }
              }
            }
          }

          PopupWindow { implicitWidth: 100; implicitHeight: 100 }

          Timer { interval: 100; running: true; repeat: true }

          Component.onCompleted: {
            Quickshell.reloadCompleted.connect(() => root.reloads++)
            Quickshell.screensChanged.connect(() => {})
          }
        }
        """;

    [Fact]
    public void Reloading_releases_the_old_generations()
    {
        using var shell = new TestShell(("shell.qml", Config), ("lib.js", ".pragma library\nfunction greet(s) { return 'hello ' + s.length }\n"));
        var platform = new FakePlatform();
        using var host = new ShellHost(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, platform, shell.Log) { WatchFiles = false };
        Assert.True(host.Start());
        shell.Loop.Process();
        var windows = platform.Windows.Count(w => !w.Disposed);
        var generations = new List<WeakReference>();
        for (var i = 0; i < 10; i++)
        {
            generations.Add(Track(host));
            host.Reload(hard: false);
            shell.Loop.Advance(TimeSpan.FromMilliseconds(250));
        }
        Assert.DoesNotContain(shell.Messages, m => !m.StartsWith("Info", StringComparison.Ordinal));
        // A reload hands each window to the new generation instead of making another.
        Assert.Equal(3, windows);
        Assert.Equal(windows, platform.Windows.Count(w => !w.Disposed));

        Collect();
        Assert.Equal(0, generations.Count(g => g.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Track(ShellHost host) => new(host.Engine);

    private static void Collect()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
