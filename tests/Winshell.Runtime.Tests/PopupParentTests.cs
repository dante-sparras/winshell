using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A popup follows the parent it is anchored to now, including after that parent changes.</summary>
public class PopupParentTests
{
    [Fact]
    public void A_popup_hides_when_its_current_parent_hides()
    {
        using var files = new TestShell(("shell.qml", """
            import Winshell
            import QtQuick

            ShellRoot {
              PanelWindow {
                id: bar
                objectName: "bar"
                implicitWidth: 200
                implicitHeight: 40
              }
              PanelWindow {
                id: other
                objectName: "other"
                implicitWidth: 200
                implicitHeight: 40
              }
              PopupWindow {
                id: pop
                objectName: "popup"
                anchor.window: bar
                visible: true
                implicitWidth: 80
                implicitHeight: 40
              }
            }
            """));
        var platform = new FakePlatform();
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, platform, files.Log);
        Assert.True(host.Start(), string.Join("\n", files.Messages));
        files.Loop.Process();

        var popup = platform.Windows.Single(w => w.Kind == WindowKind.Popup);
        var bar = Panel("bar");
        var other = Panel("other");
        Assert.True(popup.IsVisible);

        bar.Visible = false;
        files.Loop.Process();
        Assert.False(popup.IsVisible);

        bar.Visible = true;
        ((PopupWindow)popup.Client).ParentWindow = other;
        files.Loop.Process();
        Assert.True(popup.IsVisible);

        // The popup no longer listens to the parent it left.
        bar.Visible = false;
        files.Loop.Process();
        Assert.True(popup.IsVisible);

        other.Visible = false;
        files.Loop.Process();
        Assert.False(popup.IsVisible);

        PanelWindow Panel(string name) =>
            (PanelWindow)platform.Windows.Single(w => w.Kind == WindowKind.Panel && ((ShellWindow)w.Client).ObjectName == name).Client;
    }
}
