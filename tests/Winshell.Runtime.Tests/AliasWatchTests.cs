using Winshell.Runtime;
using Xunit;

namespace Winshell.Runtime.Tests;

public class AliasWatchTests
{
    [Fact]
    public void An_alias_drops_its_forwarder_when_its_owner_is_destroyed()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              id: root
              property string tag: "a"
              property QtObject child: QtObject { property alias tag: root.tag }
            }
            """));
        var root = shell.Load();
        var child = (QObject)root.ReadProperty("child")!;
        Assert.Equal("a", child.ReadProperty("tag"));
        Assert.Equal(1, root.ObserverCount("tag"));

        child.Destroy();

        Assert.Equal(0, root.ObserverCount("tag"));
        root.WriteProperty("tag", "b");
        Assert.Equal("b", root.ReadProperty("tag"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void An_alias_follows_a_replaced_intermediate_object()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              id: root
              property QtObject box: QtObject {
                id: box
                property QtObject leaf: QtObject { property string text: "one" }
              }
              property QtObject spare: QtObject { property string text: "two" }
              property alias label: box.leaf.text
              property string mirror: label
            }
            """));
        var root = shell.Load();
        var box = (QObject)root.ReadProperty("box")!;
        var leaf = (QObject)box.ReadProperty("leaf")!;
        var spare = (QObject)root.ReadProperty("spare")!;
        Assert.Equal("one", root.ReadProperty("label"));
        Assert.Equal("one", root.ReadProperty("mirror"));
        // The alias watch and the mirror binding both observe the leaf.
        Assert.Equal(2, leaf.ObserverCount("text"));

        box.WriteProperty("leaf", spare);
        shell.Loop.Process();

        Assert.Equal("two", root.ReadProperty("label"));
        Assert.Equal("two", root.ReadProperty("mirror"));
        Assert.Equal(0, leaf.ObserverCount("text"));
        Assert.Equal(2, spare.ObserverCount("text"));

        leaf.WriteProperty("text", "stale");
        spare.WriteProperty("text", "three");
        shell.Loop.Process();

        Assert.Equal("three", root.ReadProperty("label"));
        Assert.Equal("three", root.ReadProperty("mirror"));
        Assert.Equal(0, leaf.ObserverCount("text"));
        Assert.Empty(shell.Messages);
    }
}
