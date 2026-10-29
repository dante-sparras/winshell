using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class LoaderTests
{
    [Fact]
    public void A_load_passes_through_loading_and_progress_notifies()
    {
        var (root, shell) = Show("""
            import QtQuick
            Item {
              property string statuses: ""
              property string progresses: ""
              Loader {
                id: loader
                objectName: "loader"
                sourceComponent: Rectangle { implicitWidth: 4; implicitHeight: 5 }
                onStatusChanged: statuses = statuses + status + ","
                onProgressChanged: progresses = progresses + progress + ","
              }
            }
            """);
        using var _ = shell;
        var loader = (Loader)Find(root, "loader");
        Assert.Equal("2,1,", root.ReadProperty("statuses"));
        Assert.Equal("1,", root.ReadProperty("progresses"));
        Assert.Equal((int)Loader.StatusValue.Ready, loader.Status);
        Assert.Equal(1.0, loader.Progress);
        Assert.NotNull(loader.Item_);
    }

    [Fact]
    public void Destroying_the_loaded_item_clears_the_loader()
    {
        var (root, shell) = Show("""
            import QtQuick
            Item {
              property int seenStatus: loader.status
              property double seenProgress: loader.progress
              Loader {
                id: loader
                objectName: "loader"
                sourceComponent: Rectangle {}
              }
            }
            """);
        using var _ = shell;
        var loader = (Loader)Find(root, "loader");
        Assert.Equal(1, Convert.ToInt32(root.ReadProperty("seenStatus")));
        Assert.Equal(1.0, Convert.ToDouble(root.ReadProperty("seenProgress")));
        loader.Item_!.Destroy();
        shell.Loop.Process();
        Assert.Null(loader.Item_);
        Assert.Equal((int)Loader.StatusValue.Null, loader.Status);
        Assert.Equal(0.0, loader.Progress);
        Assert.Equal(0, Convert.ToInt32(root.ReadProperty("seenStatus")));
        Assert.Equal(0.0, Convert.ToDouble(root.ReadProperty("seenProgress")));
    }

    [Fact]
    public void Asynchronous_true_still_finishes_before_the_load_returns()
    {
        var (root, shell) = Show("""
            import QtQuick
            Loader {
              objectName: "loader"
              asynchronous: true
              sourceComponent: Rectangle {}
            }
            """);
        using var _ = shell;
        var loader = (Loader)Find(root, "loader");
        Assert.True(loader.Asynchronous);
        Assert.Equal((int)Loader.StatusValue.Ready, loader.Status);
        Assert.Equal(1.0, loader.Progress);
        Assert.NotNull(loader.Item_);
    }

    private static (Item Root, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(100);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, shell);
    }

    private static Item Find(Item root, string objectName)
    {
        if (root.ObjectName == objectName)
            return root;
        foreach (var child in root.ChildItems)
        {
            try
            {
                return Find(child, objectName);
            }
            catch (InvalidOperationException)
            {
            }
        }
        throw new InvalidOperationException($"No item named {objectName}");
    }
}
