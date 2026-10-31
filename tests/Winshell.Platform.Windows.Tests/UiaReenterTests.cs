using System.Text;
using Winshell.Platform.Windows.Accessibility;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// UI Automation changes the scene after the call that asked for the change, and a call that arrives during a frame
/// or during another call does not rebuild the tree.
/// </summary>
public class UiaReenterTests
{
    /// <summary>Paints once when asked, so a test can call in while a frame is drawing.</summary>
    public sealed class PaintProbe : Item
    {
        public static Action? DuringPaint;

        public override void Paint(SkiaSharp.SKCanvas canvas, PaintContext context)
        {
            DuringPaint?.Invoke();
            base.Paint(canvas, context);
        }
    }

    private const string Config = """
        import Winshell
        import QtQuick
        import QtQuick.Controls
        import Probe
        PanelWindow {
          id: shell
          anchors { top: true; left: true }
          exclusionMode: ExclusionMode.Ignore
          implicitWidth: 120
          implicitHeight: 80
          property int moves: 0
          Slider {
            objectName: "slider"
            from: 0
            to: 100
            value: 10
            onMoved: shell.moves = shell.moves + 1
          }
          TextInput {
            objectName: "field"
            text: "abcd"
          }
          Flickable {
            objectName: "pane"
            width: 40
            height: 40
            contentWidth: 40
            contentHeight: 200
          }
          PaintProbe { anchors.fill: parent }
        }
        """;

    [Fact]
    public void A_change_lands_after_the_call_that_asked_for_it()
    {
        Run(window =>
        {
            var shell = (ShellWindow)window.Client;
            var root = shell.Scene.Root;
            var slider = (Slider)Find(root, "slider");
            var field = (TextInput)Find(root, "field");
            var pane = (Flickable)Find(root, "pane");
            var sliderElement = window.Automation.ElementFor(slider);
            var fieldElement = window.Automation.ElementFor(field);
            var paneElement = window.Automation.ElementFor(pane);
            Assert.NotNull(sliderElement);
            Assert.NotNull(fieldElement);
            Assert.NotNull(paneElement);

            var moved = ((IRangeValueProvider)sliderElement).SetValue(40);
            var rejected = ((IRangeValueProvider)sliderElement).SetValue(1000);
            Assert.Equal(Uia.S_OK, ((ITextProvider)fieldElement).get_DocumentRange(out var range));
            var selected = range!.Select();
            var scrolled = ((IScrollProvider)paneElement).Scroll(2, 4);

            Assert.Equal(Uia.S_OK, moved);
            Assert.Equal(Uia.E_INVALIDARG, rejected);
            Assert.Equal(Uia.S_OK, selected);
            Assert.Equal(Uia.S_OK, scrolled);
            Assert.Equal(10d, slider.Value);
            Assert.Equal(0d, Convert.ToDouble(shell.ReadProperty("moves")));
            Assert.Equal(0, field.SelectionEnd);
            Assert.Equal(0d, pane.ContentY);
            return false;
        }, window =>
        {
            var shell = (ShellWindow)window.Client;
            var root = shell.Scene.Root;
            var slider = (Slider)Find(root, "slider");
            var field = (TextInput)Find(root, "field");
            var pane = (Flickable)Find(root, "pane");
            Assert.Equal(40d, slider.Value);
            Assert.Equal(1d, Convert.ToDouble(shell.ReadProperty("moves")));
            Assert.Equal(4, field.SelectionEnd);
            Assert.Equal(48d, pane.ContentY);
        });
    }

    [Fact]
    public void A_nested_call_and_a_frame_do_not_refresh_the_tree()
    {
        var duringPaint = -1;
        var expected = -1;
        WindowAutomation? automation = null;
        WaitForFrame(window =>
        {
            automation = window.Automation;
            var scene = ((ShellWindow)window.Client).Scene;
            var root = scene.Root;
            automation.Listening = () => false;
            Assert.NotNull(automation.ElementFor(root));
            automation.AfterFrame();
            var before = automation.SyncCount;
            var atOuter = -1;
            var atInner = -1;
            automation.Run(() =>
            {
                atOuter = automation.SyncCount;
                atInner = automation.Run(() => automation.SyncCount);
                return 0;
            });
            Assert.Equal(before + 1, atOuter);
            Assert.Equal(before + 1, atInner);
            Assert.Equal(before + 1, automation.SyncCount);

            expected = automation.SyncCount;
            automation.AfterFrame();
            PaintProbe.DuringPaint = () =>
            {
                if (duringPaint >= 0)
                    return;
                duringPaint = automation.Run(() => automation.SyncCount);
            };
            scene.RequestUpdate();
        }, () => duringPaint >= 0, _ =>
        {
            Assert.Equal(expected, duringPaint);
            Assert.Equal(expected, automation!.SyncCount);
        });
    }

    /// <summary>
    /// Runs <paramref name="setup"/> on one turn, then waits until <paramref name="ready"/> or 200 turns.
    /// A wall-clock deadline can come due in the same batch as setup and quit before the frame is drawn.
    /// </summary>
    private static void WaitForFrame(Action<Win32Window> setup, Func<bool> ready, Action<Win32Window> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), Config);
        var notes = new StringBuilder();
        var log = new Log { Sink = (_, _, message) => notes.AppendLine(message) };
        PaintProbe.DuringPaint = null;
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(Path.Combine(dir, "shell.qml"), loop, platform, log, () =>
            {
                var modules = StandardModules.Create();
                modules.Module("Probe").Type<PaintProbe>("PaintProbe");
                return modules;
            })
            { WatchFiles = false };
            Assert.True(host.Start());
            var window = Assert.Single(platform.Windows);
            var armed = false;
            var turns = 0;
            void Step()
            {
                if (!armed)
                {
                    armed = true;
                    setup(window);
                    loop.Post(Step);
                    return;
                }
                if (ready() || ++turns >= 200)
                {
                    check(window);
                    loop.Quit(0);
                    return;
                }
                loop.Post(Step);
            }
            loop.Post(Step);
            Assert.True(loop.Run() == 0, notes.ToString());
        }
        finally
        {
            PaintProbe.DuringPaint = null;
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Run(Func<Win32Window, bool> first, Action<Win32Window> then)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), Config);
        var notes = new StringBuilder();
        var log = new Log { Sink = (_, _, message) => notes.AppendLine(message) };
        PaintProbe.DuringPaint = null;
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(Path.Combine(dir, "shell.qml"), loop, platform, log, () =>
            {
                var modules = StandardModules.Create();
                modules.Module("Probe").Type<PaintProbe>("PaintProbe");
                return modules;
            })
            { WatchFiles = false };
            Assert.True(host.Start());
            var window = Assert.Single(platform.Windows);
            var saw = false;
            var started = false;
            loop.Schedule(TimeSpan.FromMilliseconds(100), () =>
            {
                saw = true;
                // This batch can already contain the outer quit: a busy loop runs every due timer after this
                // callback, before a timer scheduled here. Started means that quit must not beat the follow-up.
                started = true;
                if (first(window))
                {
                    loop.Quit(0);
                    return;
                }
                var finished = false;
                var hang = loop.Schedule(TimeSpan.FromSeconds(2), () =>
                {
                    if (!finished)
                        loop.Quit(1);
                });
                loop.Schedule(TimeSpan.FromMilliseconds(80), () =>
                {
                    finished = true;
                    hang.Dispose();
                    then(window);
                    loop.Quit(0);
                });
            });
            loop.Schedule(TimeSpan.FromMilliseconds(2000), () =>
            {
                if (!started)
                    loop.Quit(1);
            });
            Assert.True(loop.Run() == 0, notes.ToString());
            Assert.True(saw);
        }
        finally
        {
            PaintProbe.DuringPaint = null;
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Item Find(Item item, string name) =>
        FindOrNull(item, name) ?? throw new InvalidOperationException($"No item named {name}.");

    private static Item? FindOrNull(Item item, string name)
    {
        if (item.ObjectName == name)
            return item;
        foreach (var child in item.ChildItems)
        {
            if (FindOrNull(child, name) is { } found)
                return found;
        }
        return null;
    }
}
