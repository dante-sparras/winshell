using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class GridLayoutTests
{
    [Fact]
    public void A_centered_item_keeps_its_side_margins_inside_the_cell()
    {
        var (grid, shell) = Load("""
            import QtQuick
            import QtQuick.Layouts
            GridLayout {
              objectName: "grid"
              width: 100
              height: 20
              columns: 1
              columnSpacing: 0
              rowSpacing: 0
              Rectangle { Layout.preferredWidth: 100; implicitHeight: 10 }
              Rectangle {
                objectName: "box"
                Layout.preferredWidth: 20
                Layout.leftMargin: 10
                Layout.rightMargin: 6
                Layout.alignment: Qt.AlignHCenter
                implicitHeight: 10
              }
            }
            """);
        using var _ = shell;
        var box = Named(grid, "box");
        Assert.Equal(20, box.Width);
        Assert.Equal(42, box.X);
    }

    [Fact]
    public void A_right_aligned_item_keeps_its_right_margin()
    {
        var (grid, shell) = Load("""
            import QtQuick
            import QtQuick.Layouts
            GridLayout {
              width: 100
              height: 20
              columns: 1
              columnSpacing: 0
              rowSpacing: 0
              Rectangle { Layout.preferredWidth: 100; implicitHeight: 10 }
              Rectangle {
                objectName: "box"
                Layout.preferredWidth: 20
                Layout.leftMargin: 10
                Layout.rightMargin: 6
                Layout.alignment: Qt.AlignRight
                implicitHeight: 10
              }
            }
            """);
        using var _ = shell;
        var box = Named(grid, "box");
        Assert.Equal(20, box.Width);
        Assert.Equal(74, box.X);
    }

    [Fact]
    public void A_bottom_aligned_item_keeps_its_bottom_margin()
    {
        var (grid, shell) = Load("""
            import QtQuick
            import QtQuick.Layouts
            GridLayout {
              width: 40
              height: 50
              columns: 2
              columnSpacing: 0
              rowSpacing: 0
              Rectangle { Layout.preferredWidth: 10; Layout.preferredHeight: 50 }
              Rectangle {
                objectName: "box"
                Layout.preferredWidth: 10
                Layout.preferredHeight: 10
                Layout.topMargin: 4
                Layout.bottomMargin: 8
                Layout.alignment: Qt.AlignBottom
                implicitHeight: 10
              }
            }
            """);
        using var _ = shell;
        var box = Named(grid, "box");
        Assert.Equal(10, box.Height);
        Assert.Equal(32, box.Y);
    }

    [Fact]
    public void A_span_keeps_the_minimum_and_counts_the_spacing_between_its_cells()
    {
        var (grid, shell) = Load("""
            import QtQuick
            import QtQuick.Layouts
            GridLayout {
              objectName: "grid"
              columns: 2
              columnSpacing: 10
              rowSpacing: 0
              Rectangle {
                objectName: "across"
                Layout.columnSpan: 2
                Layout.minimumWidth: 80
                implicitWidth: 10
                implicitHeight: 10
              }
            }
            """);
        using var _ = shell;
        Assert.Equal(80, grid.ImplicitWidth);
        Assert.Equal(80, Named(grid, "across").Width);
    }

    [Fact]
    public void A_row_span_sets_the_height_of_the_rows_it_covers()
    {
        var (grid, shell) = Load("""
            import QtQuick
            import QtQuick.Layouts
            GridLayout {
              objectName: "grid"
              columns: 1
              rows: 2
              columnSpacing: 0
              rowSpacing: 4
              Rectangle {
                objectName: "down"
                Layout.rowSpan: 2
                Layout.preferredHeight: 60
                implicitWidth: 10
                implicitHeight: 10
              }
            }
            """);
        using var _ = shell;
        Assert.Equal(60, grid.ImplicitHeight);
        Assert.Equal(60, Named(grid, "down").Height);
    }

    [Fact]
    public void Horizontal_stretch_shares_extra_width()
    {
        var (grid, shell) = Load("""
            import QtQuick
            import QtQuick.Layouts
            GridLayout {
              width: 200
              height: 20
              columns: 2
              columnSpacing: 0
              rowSpacing: 0
              Rectangle {
                objectName: "narrow"
                Layout.fillWidth: true
                Layout.preferredWidth: 40
                Layout.horizontalStretchFactor: 1
                implicitHeight: 10
              }
              Rectangle {
                objectName: "wide"
                Layout.fillWidth: true
                Layout.preferredWidth: 40
                Layout.horizontalStretchFactor: 3
                implicitHeight: 10
              }
            }
            """);
        using var _ = shell;
        Assert.Equal((0.0, 70.0), (Named(grid, "narrow").X, Named(grid, "narrow").Width));
        Assert.Equal((70.0, 130.0), (Named(grid, "wide").X, Named(grid, "wide").Width));
    }

    [Fact]
    public void Vertical_stretch_shares_extra_height()
    {
        var (grid, shell) = Load("""
            import QtQuick
            import QtQuick.Layouts
            GridLayout {
              width: 20
              height: 200
              columns: 1
              columnSpacing: 0
              rowSpacing: 0
              Rectangle {
                objectName: "short"
                Layout.fillHeight: true
                Layout.preferredHeight: 40
                Layout.verticalStretchFactor: 1
                implicitWidth: 10
              }
              Rectangle {
                objectName: "tall"
                Layout.fillHeight: true
                Layout.preferredHeight: 40
                Layout.verticalStretchFactor: 3
                implicitWidth: 10
              }
            }
            """);
        using var _ = shell;
        Assert.Equal((0.0, 70.0), (Named(grid, "short").Y, Named(grid, "short").Height));
        Assert.Equal((70.0, 130.0), (Named(grid, "tall").Y, Named(grid, "tall").Height));
    }

    [Fact]
    public void Right_to_left_packs_columns_from_the_right()
    {
        var (grid, shell) = Load("""
            import QtQuick
            import QtQuick.Layouts
            GridLayout {
              width: 100
              height: 20
              layoutDirection: Qt.RightToLeft
              columns: 2
              columnSpacing: 0
              rowSpacing: 0
              Rectangle { objectName: "a"; implicitWidth: 40; implicitHeight: 10 }
              Rectangle { objectName: "b"; implicitWidth: 60; implicitHeight: 10 }
            }
            """);
        using var _ = shell;
        Assert.Equal((60.0, 40.0), (Named(grid, "a").X, Named(grid, "a").Width));
        Assert.Equal((0.0, 60.0), (Named(grid, "b").X, Named(grid, "b").Width));
    }

    private static (Item Grid, TestShell Shell) Load(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var grid = (Item)shell.Load();
        shell.Loop.Process();
        PolishQueue.Flush();
        return (grid, shell);
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
