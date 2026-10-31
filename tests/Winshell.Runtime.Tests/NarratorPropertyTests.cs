using Winshell.Runtime.Quick;
using Role = Winshell.Runtime.Quick.AccessibleAttached.RoleValue;

namespace Winshell.Runtime.Tests;

/// <summary>Heading level, dialog, notification, and position in a set of the same role.</summary>
public class NarratorPropertyTests
{
    [Fact]
    public void Headings_dialogs_and_sets_say_what_the_role_implies()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            import QtQuick.Controls
            Column {
              Text { objectName: "title"; text: "Network"; Accessible.role: Accessible.Heading; Accessible.headingLevel: 1 }
              Text { objectName: "section"; text: "Wi-Fi"; Accessible.role: Accessible.Heading }
              Text { objectName: "odd"; text: "No"; Accessible.role: Accessible.Heading; Accessible.headingLevel: 12 }
              Item { objectName: "one"; Accessible.role: Accessible.ListItem; Accessible.name: "One"; width: 8; height: 8 }
              Button { text: "Skip" }
              Item { objectName: "two"; Accessible.role: Accessible.ListItem; Accessible.name: "Two"; width: 8; height: 8 }
              Item { objectName: "open"; Accessible.role: Accessible.MenuItem; Accessible.name: "Open"; width: 8; height: 8 }
              Item { objectName: "close"; Accessible.role: Accessible.MenuItem; Accessible.name: "Close"; width: 8; height: 8 }
              Item { objectName: "ask"; Accessible.role: Accessible.Dialog; Accessible.name: "Save?"; width: 8; height: 8 }
              Item { objectName: "toast"; Accessible.role: Accessible.Notification; Accessible.name: "Saved"; width: 8; height: 8 }
              TabBar {
                TabButton { objectName: "first"; text: "A" }
                TabButton { objectName: "second"; text: "B" }
              }
            }
            """));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(400);
        scene.Root.SetLayoutHeight(400);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        var tree = new AccessibleTree(scene);
        tree.Refresh();
        var nodes = Descendants(tree.Root).ToList();

        AccessibleNode Find(string name) => nodes.First(n => n.Item.ObjectName == name);

        Assert.Equal(Role.Heading, Find("title").Role);
        Assert.Equal(1, Find("title").HeadingLevel);
        Assert.Equal(2, Find("section").HeadingLevel);
        Assert.Equal(2, Find("odd").HeadingLevel);

        Assert.Equal((1, 2), Find("one").PlaceInSet);
        Assert.Equal((2, 2), Find("two").PlaceInSet);
        Assert.Null(nodes.Single(n => n.Name == "Skip").PlaceInSet);

        Assert.Equal((1, 2), Find("open").PlaceInSet);
        Assert.Equal((2, 2), Find("close").PlaceInSet);

        Assert.Equal(Role.Dialog, Find("ask").Role);
        Assert.Equal(Role.Notification, Find("toast").Role);

        Assert.Equal(Role.PageTab, Find("first").Role);
        Assert.Equal((1, 2), Find("first").PlaceInSet);
        Assert.Equal((2, 2), Find("second").PlaceInSet);
    }

    private static IEnumerable<AccessibleNode> Descendants(AccessibleNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var further in Descendants(child))
                yield return further;
        }
    }
}
