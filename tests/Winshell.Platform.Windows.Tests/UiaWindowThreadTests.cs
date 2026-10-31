using System.Diagnostics;
using Winshell.Platform.Windows.Accessibility;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// UI Automation calls <c>get_FragmentRoot</c>, runtime ids and text-range comparison on its own threads.
/// Those reads have to happen on the window thread, and a disconnected element has to say it is gone.
/// </summary>
public class UiaWindowThreadTests
{
    private const string Config = """
        import QtQuick
        import QtQuick.Controls
        import Winshell
        ShellRoot {
          PanelWindow {
            visible: false
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 200
            implicitHeight: 80
            TextField { objectName: "field"; text: "Hello"; width: 120; height: 32 }
          }
        }
        """;

    [Fact]
    public void Fragment_root_and_text_ranges_run_on_the_window_thread()
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
            UiaElement? element = null;
            ITextRangeProvider? stale = null;
            ITextRangeProvider? fresh = null;
            var ready = new ManualResetEventSlim(false);
            var done = new ManualResetEventSlim(false);
            int rootHr = -1, compare = -1;
            string? cloneText = null;
            var treeRejected = false;
            Exception? failure = null;

            loop.Schedule(TimeSpan.FromMilliseconds(50), () =>
            {
                var field = Find(shell.ContentItem, "field");
                Assert.NotNull(field);
                ((TextInput)field).Text_ = "Hello";
                element = native.Automation.ElementFor(field);
                Assert.NotNull(element);
                var text = (ITextProvider)element;
                Assert.Equal(Uia.S_OK, text.get_DocumentRange(out stale));
                ((TextInput)field).Text_ = "Hi";
                Assert.Equal(Uia.S_OK, text.get_DocumentRange(out fresh));
                ready.Set();
            });
            var started = 0L;
            loop.Schedule(TimeSpan.FromMilliseconds(200), () =>
            {
                var worker = new Thread(() =>
                {
                    try
                    {
                        var probe = native.Automation.Run(() => 7);
                        if (probe != 7)
                            failure = new InvalidOperationException($"probe {probe}");
                        try
                        {
                            native.Automation.ElementFor(shell.ContentItem);
                        }
                        catch (InvalidOperationException)
                        {
                            treeRejected = true;
                        }
                        rootHr = ((IRawElementProviderFragment)element!).get_FragmentRoot(out var root);
                        var compareHr = ((ITextRangeProvider)stale!).CompareEndpoints(Uia.EndpointEnd, fresh!, Uia.EndpointEnd, out compare);
                        var cloneHr = ((ITextRangeProvider)stale).Clone(out var clone);
                        if (cloneHr == Uia.S_OK && clone is not null)
                            clone.GetText(-1, out cloneText);
                        if (root is null || rootHr != Uia.S_OK || compareHr != Uia.S_OK)
                            failure = new InvalidOperationException($"root {rootHr} {root is not null}, compare {compareHr} {compare}");
                    }
                    catch (Exception e)
                    {
                        failure = e;
                    }
                    finally
                    {
                        done.Set();
                    }
                })
                { IsBackground = true };
                started = Stopwatch.GetTimestamp();
                worker.Start();
            });
            void Watch()
            {
                if (done.IsSet)
                    loop.Quit(0);
                // Several calls, each bounded by its own timeout. This only stops a call that never returns,
                // so a timed-out call still fails below on its result.
                else if (started != 0 && Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(45))
                    loop.Quit(1);
                else
                    loop.Schedule(TimeSpan.FromMilliseconds(50), Watch);
            }
            loop.Schedule(TimeSpan.FromMilliseconds(250), Watch);
            Assert.True(loop.Run() == 0, "The call did not finish.");

            Assert.True(ready.IsSet);
            Assert.True(done.IsSet);
            Assert.Null(failure);
            Assert.True(treeRejected);
            Assert.Equal(Uia.S_OK, rootHr);
            Assert.Equal(0, compare);
            Assert.Equal("Hi", cloneText);

            Assert.NotNull(element);
            element.Detach();
            var hr = ((IRawElementProviderFragment)element).GetRuntimeId(out var id);
            Assert.Equal(Uia.ElementNotAvailable, hr);
            Assert.Null(id);
            var rootAfter = 0;
            IRawElementProviderFragmentRoot? gone = null;
            var call = TimeSpan.Zero;
            var detached = new Thread(() =>
            {
                var watch = Stopwatch.StartNew();
                rootAfter = ((IRawElementProviderFragment)element).get_FragmentRoot(out gone);
                call = watch.Elapsed;
            })
            { IsBackground = true };
            detached.Start();
            Assert.True(detached.Join(TimeSpan.FromSeconds(15)), "The disconnected call did not return.");
            Assert.True(call < TimeSpan.FromSeconds(1), $"get_FragmentRoot took {call.TotalMilliseconds:0} ms");
            Assert.Equal(Uia.ElementNotAvailable, rootAfter);
            Assert.Null(gone);
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
            if (Find(child, name) is { } found)
                return found;
        }
        return null;
    }
}
