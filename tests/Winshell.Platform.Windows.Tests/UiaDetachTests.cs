using System.Reflection;
using Winshell.Platform.Windows.Accessibility;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A client can hold a provider after its item leaves the tree. That provider has to let go of the item, and a menu
/// has to say that it closed.
/// </summary>
public class UiaDetachTests
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
            Item {
              objectName: "menu"
              width: 20; height: 20
              Accessible.role: Accessible.PopupMenu
              Accessible.name: "File"
            }
            Item {
              objectName: "go"
              width: 20; height: 20
              Accessible.role: Accessible.Button
              Accessible.name: "Go"
            }
          }
        }
        """;

    [Fact]
    public void A_removed_element_is_disconnected_and_a_menu_says_it_closed()
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
            Exception? failure = null;
            var finished = false;
            loop.Schedule(TimeSpan.FromMilliseconds(50), () =>
            {
                try
                {
                    var native = Assert.Single(platform.Windows);
                    var shell = (ShellWindow)native.Client;
                    var automation = native.Automation;
                    var order = new List<(IRawElementProviderSimple Provider, string Step)>();
                    // Nobody is subscribed. Removal still has to disconnect, which is what keeps a generation alive.
                    automation.Listening = () => false;
                    automation.Disconnect = provider => order.Add((provider, "disconnect"));
                    automation.RaiseEvent = (provider, id) =>
                    {
                        if (id == Uia.MenuClosedEvent)
                            order.Add((provider, "closed"));
                    };
                    var menuItem = Find(shell.ContentItem, "menu");
                    var goItem = Find(shell.ContentItem, "go");
                    Assert.NotNull(menuItem);
                    Assert.NotNull(goItem);
                    var menu = automation.ElementFor(menuItem);
                    var go = automation.ElementFor(goItem);
                    Assert.NotNull(menu);
                    Assert.NotNull(go);
                    var menuNode = NodeOf(menu);
                    var goNode = NodeOf(go);
                    menuItem.Visible = false;
                    goItem.Visible = false;
                    automation.AfterFrame();
                    Assert.Equal(0, automation.Run(() => 0));

                    Assert.Equal(new[] { "closed", "disconnect" }, Steps(order, menu));
                    Assert.Equal(new[] { "disconnect" }, Steps(order, go));
                    Assert.Null(Field(menu, "_node"));
                    Assert.Null(Field(menu, "_automation"));
                    Assert.Null(Field(go, "_node"));
                    Assert.Null(Field(go, "_automation"));
                    AssertCollected(menuNode);
                    AssertCollected(goNode);
                    finished = true;
                }
                catch (Exception e)
                {
                    failure = e;
                }
                loop.Quit(0);
            });
            loop.Schedule(TimeSpan.FromMilliseconds(4000), () => loop.Quit(0));
            loop.Run();
            Assert.Null(failure?.ToString());
            Assert.True(finished);
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

    private static string[] Steps(List<(IRawElementProviderSimple Provider, string Step)> order, IRawElementProviderSimple provider) =>
        order.Where(step => ReferenceEquals(step.Provider, provider)).Select(step => step.Step).ToArray();

    private static object? Field(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance);

    /// <summary>The node has to be read in a method that has already returned, or a debug build keeps it alive.</summary>
    private static WeakReference NodeOf(UiaElement element) => new(Field(element, "_node")!);

    private static void AssertCollected(WeakReference reference)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
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
