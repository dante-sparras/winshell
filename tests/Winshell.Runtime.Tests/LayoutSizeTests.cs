using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class LayoutSizeTests
{
    [Fact]
    public void An_explicit_width_stays_preferred_after_the_layout_grows_the_item()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            import QtQuick.Layouts
            Item {
              id: host
              objectName: "host"
              property int wide: 400
              width: wide
              height: 30
              RowLayout {
                objectName: "row"
                anchors.fill: parent
                spacing: 0
                Rectangle { objectName: "box"; width: 100; Layout.fillWidth: true; implicitHeight: 10 }
              }
            }
            """));
        var root = (Item)shell.Load();
        using var _ = shell;
        Polish(shell);
        Polish(shell);
        var row = Named(root, "row");
        var box = Named(row, "box");
        Assert.Equal(400, box.Width);
        Assert.Equal(100, row.ImplicitWidth);

        shell.Eval(root, "wide = 120");
        Polish(shell);
        Polish(shell);
        Assert.Equal(120, box.Width);
        Assert.Equal(100, row.ImplicitWidth);
    }

    [Fact]
    public void A_nested_layout_fills_unless_fill_was_set()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            import QtQuick.Layouts
            ColumnLayout {
              objectName: "col"
              width: 200
              height: 80
              spacing: 0
              RowLayout {
                objectName: "fill"
                spacing: 0
                Rectangle { objectName: "grow"; Layout.fillWidth: true; implicitWidth: 10; implicitHeight: 10 }
                Rectangle { objectName: "fixed"; implicitWidth: 30; implicitHeight: 10 }
              }
              RowLayout {
                objectName: "fixedRow"
                Layout.fillWidth: false
                spacing: 0
                Rectangle { implicitWidth: 40; implicitHeight: 10 }
              }
            }
            """));
        var root = (Item)shell.Load();
        using var _ = shell;
        Polish(shell);
        var fill = Named(root, "fill");
        var fixedRow = Named(root, "fixedRow");
        Assert.Equal(200, fill.Width);
        Assert.Equal(170, Named(fill, "grow").Width);
        Assert.Equal(30, Named(fill, "fixed").Width);
        Assert.Equal(40, fixedRow.Width);
    }

    private static void Polish(TestShell shell)
    {
        shell.Loop.Process();
        PolishQueue.Flush();
    }

    private static Item Named(Item root, string objectName) =>
        root.ObjectName == objectName ? root : root.ChildItems.Select(child => Try(child, objectName)).First(item => item is not null)!;

    private static Item? Try(Item item, string objectName)
    {
        if (item.ObjectName == objectName)
            return item;
        foreach (var child in item.ChildItems)
        {
            var found = Try(child, objectName);
            if (found is not null)
                return found;
        }
        return null;
    }
}
