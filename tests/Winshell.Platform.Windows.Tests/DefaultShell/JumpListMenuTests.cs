using Winshell.Platform.Windows.Accessibility;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>The task menu's destinations. Fakes supply the lists, so nothing here reads this PC's profile.</summary>
public class JumpListMenuTests
{
    private const string Tasks = """
        import QtQuick
        import Winshell.Windows
        import Winshell.Apps
        TaskList {
          windows: WindowList.windows.values
          activeWindow: WindowList.activeWindow
          apps: Apps
          pinned: ["File Explorer"]
        }
        """;

    [Fact]
    public void Destinations_are_shown_above_pin_and_close_and_choosing_one_does_not_launch()
    {
        var desk = new Desk();
        desk.Services.JumpLists.Set("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Budget.xlsx", "Notes.txt");
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var menu = Open(shell, "app:microsoft.windowsterminal_8wekyb3d8bbwe!app");

        Assert.Equal(
            ["Budget.xlsx", "Notes.txt", "Terminal", "Pin to taskbar", "Close window"],
            Labels(menu));
        Click(menu, "dest:0");
        shell.Settle();
        Assert.True(menu.Visible);
        Assert.Equal(0, desk.Terminal.Launches);
        Assert.Empty(desk.Shell.Calls);
        Assert.Equal(["Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"], desk.Services.JumpLists.Asked);
    }

    [Fact]
    public void An_unreadable_list_is_a_sentence_and_an_empty_list_adds_nothing()
    {
        var desk = new Desk();
        desk.Services.JumpLists.Unreadable("Microsoft.Windows.Explorer");
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);

        var explorer = Open(shell, "app:file explorer");
        var labels = Labels(explorer);
        Assert.Equal(["Jump list can't be read", "File Explorer", "Unpin from taskbar", "Close window"], labels);
        Assert.DoesNotContain(labels, label => label is "Recent" or "Frequent" or "Tasks");

        var terminal = Open(shell, "app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
        Assert.Equal(["Terminal", "Pin to taskbar", "Close window"], Labels(terminal));
    }

    private static PopupWindow Open(DefaultShellHarness shell, string key)
    {
        var button = shell.Find("task:" + key);
        var at = button.MapToScene(button.Width / 2, button.Height / 2);
        shell.Scene.PointerPress(at.X, at.Y, QtEnums.RightButton);
        shell.Scene.PointerRelease(at.X, at.Y, QtEnums.RightButton);
        shell.Settle();
        var menu = (PopupWindow)shell.Root.ReadProperty("menu")!;
        Assert.True(menu.Visible);
        return menu;
    }

    private static void Click(PopupWindow menu, string id)
    {
        var row = DefaultShellHarness.FindAll(menu.Scene.Root).First(i => i.ObjectName == "menu:" + id);
        var at = row.MapToScene(row.Width / 2, row.Height / 2);
        menu.Scene.PointerPress(at.X, at.Y, QtEnums.LeftButton);
        menu.Scene.PointerRelease(at.X, at.Y, QtEnums.LeftButton);
    }

    private static string[] Labels(PopupWindow menu)
    {
        var tree = new AccessibleTree(menu.Scene);
        tree.Refresh();
        return DefaultShellHarness.FindAll(menu.Scene.Root)
            .Where(i => i.ObjectName.StartsWith("menu:", StringComparison.Ordinal))
            .OrderBy(i => i.MapToScene(0, 0).Y)
            .Select(i => tree.Find(i)!)
            .Where(n => n.Role == AccessibleAttached.RoleValue.MenuItem)
            .Select(n => n.Name)
            .ToArray();
    }

    private sealed class Desk
    {
        public Desk()
        {
            Services.Apps.Add("File Explorer", "File Explorer", "Microsoft.Windows.Explorer");
            Terminal = Services.Apps.Add("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Terminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App");
            Add(new FakeAppWindow("Documents", "Microsoft.Windows.Explorer", @"C:\Windows\explorer.exe"));
            Shell = Add(new FakeAppWindow("PowerShell", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
            Services.WindowList.Activate(Shell);
        }

        public FakeServices Services { get; } = new();
        public FakeAppEntry Terminal { get; }
        public FakeAppWindow Shell { get; }

        private FakeAppWindow Add(FakeAppWindow window)
        {
            Services.WindowList.Windows.Add(window);
            return window;
        }
    }
}
