using Winshell.Runtime.Quick;
using Role = Winshell.Runtime.Quick.AccessibleAttached.RoleValue;

namespace Winshell.Runtime.Tests;

public class AccessibilityTests
{
    private static (Item Root, Scene Scene, AccessibleTree Tree, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(400);
        scene.Root.SetLayoutHeight(400);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        var tree = new AccessibleTree(scene);
        tree.Refresh();
        return (root, scene, tree, shell);
    }

    private static string Describe(AccessibleNode node) =>
        string.Join(", ", node.Children.Select(c => c.Children.Count == 0 ? c.ToString() : $"{c} [{Describe(c)}]"));

    [Fact]
    public void Controls_and_text_are_nodes_and_layout_items_are_not()
    {
        var (_, _, tree, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              Button { text: "Save" }
              Switch { text: "Wi-Fi" }
              CheckBox { text: "Remember" }
              Slider { value: 0.5 }
              TextField { placeholderText: "Search" }
              Text { text: "Hello" }
              Rectangle { width: 10; height: 10; Text { text: "Inner" } }
              Text { text: "" }
            }
            """);
        using var _ = shell;
        Assert.Equal(
            "Button \"Save\", Switch \"Wi-Fi\", CheckBox \"Remember\", Slider \"\", EditableText \"Search\", StaticText \"Hello\", StaticText \"Inner\"",
            Describe(tree.Root));
        Assert.Equal(Role.Pane, tree.Root.Role);
    }

    [Fact]
    public void Accessible_gives_an_item_a_role_a_name_and_an_action()
    {
        var (root, _, tree, shell) = Show("""
            import QtQuick
            Item {
              property int presses: 0
              MouseArea {
                width: 20; height: 20
                Accessible.role: Accessible.Button
                Accessible.name: "Close"
                Accessible.description: "Closes the panel"
                Accessible.onPressAction: presses += 1
                Text { text: "x" }
              }
              Text { text: "Hidden"; Accessible.ignored: true }
            }
            """);
        using var _ = shell;
        var close = Assert.Single(tree.Root.Children);
        Assert.Equal(Role.Button, close.Role);
        Assert.Equal("Close", close.Name);
        Assert.Equal("Closes the panel", close.Description);
        Assert.True(close.CanInvoke);
        close.Invoke();
        Assert.Equal(1, root.ReadProperty("presses"));
    }

    [Fact]
    public void A_button_built_from_items_is_named_by_the_text_inside_it()
    {
        var (_, _, tree, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              Button { contentItem: Text { text: "Restart" } }
              ToolButton { icon.name: "settings"; ToolTip.text: "Settings" }
            }
            """);
        using var _ = shell;
        Assert.Equal("Button \"Restart\", Button \"Settings\"", Describe(tree.Root));
    }

    [Fact]
    public void Patterns_read_and_change_the_controls()
    {
        var (root, _, tree, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              property alias wifi: wifi.checked
              property alias volume: slider.value
              property alias query: field.text
              Switch { id: wifi; text: "Wi-Fi" }
              Slider { id: slider; from: 0; to: 100; stepSize: 5; value: 20 }
              TextField { id: field; text: "abc" }
              TextField { echoMode: TextInput.Password; text: "secret" }
              ProgressBar { value: 0.25 }
            }
            """);
        using var _ = shell;
        var (toggle, range, edit, password, progress) = (tree.Root.Children[0], tree.Root.Children[1], tree.Root.Children[2], tree.Root.Children[3], tree.Root.Children[4]);

        Assert.Equal(ToggleState.Off, toggle.Toggle);
        Assert.False(toggle.CanInvoke);
        toggle.DoToggle();
        Assert.True((bool)root.ReadProperty("wifi")!);
        Assert.Equal(ToggleState.On, toggle.Toggle);

        Assert.Equal(new RangeInfo(20, 0, 100, 5, 10, false), range.Range);
        range.SetRangeValue(45);
        Assert.Equal(45.0, root.ReadProperty("volume"));

        Assert.Equal("abc", edit.Value);
        Assert.False(edit.IsValueReadOnly);
        edit.SetValue("abcd");
        Assert.Equal("abcd", root.ReadProperty("query"));
        Assert.NotNull(edit.TextInput);

        Assert.True(password.IsPassword);
        Assert.Equal("", password.Value);

        Assert.True(progress.Range!.IsReadOnly);
        Assert.Equal(0.25, progress.Range.Value);
    }

    [Fact]
    public void A_combo_box_expands_into_a_list_whose_rows_select()
    {
        var (root, _, tree, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias index: box.currentIndex
              ComboBox { id: box; model: ["First", "Second", "Third"] }
            }
            """);
        using var _ = shell;
        var box = Assert.Single(tree.Root.Children);
        Assert.Equal(Role.ComboBox, box.Role);
        Assert.Equal("First", box.Value);
        Assert.False(box.IsExpanded);

        box.SetExpanded(true);
        PolishQueue.Flush();
        tree.Refresh();
        Assert.True(box.IsExpanded);
        var list = tree.Root.Children.Single(n => n.Role == Role.List);
        Assert.Equal("ListItem \"First\", ListItem \"Second\", ListItem \"Third\"", Describe(list));
        Assert.True(list.IsSelectionContainer);
        Assert.Equal([list.Children[0]], list.Selection);
        Assert.Equal(list, list.Children[1].SelectionContainer);

        list.Children[1].Select();
        tree.Refresh();
        Assert.Equal(1, root.ReadProperty("index"));
        Assert.False(box.IsExpanded);
        Assert.Equal("Second", box.Value);
    }

    [Fact]
    public void Radio_buttons_and_tabs_are_selection_items()
    {
        var (_, _, tree, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              RadioButton { text: "Light"; checked: true }
              RadioButton { text: "Dark" }
              TabBar { TabButton { text: "General" } TabButton { text: "Sound" } }
            }
            """);
        using var _ = shell;
        var (light, dark, tabs) = (tree.Root.Children[0], tree.Root.Children[1], tree.Root.Children[2]);
        Assert.True(light.IsSelected);
        Assert.Null(light.Toggle);
        dark.Select();
        Assert.False(light.IsSelected);
        Assert.True(dark.IsSelected);

        Assert.Equal(Role.PageTabList, tabs.Role);
        Assert.Equal("PageTab \"General\", PageTab \"Sound\"", Describe(tabs));
        Assert.True(tabs.IsSelectionRequired);
        tabs.Children[1].Select();
        Assert.Equal([tabs.Children[1]], tabs.Selection);
    }

    [Fact]
    public void Refresh_reports_what_changed()
    {
        var (root, _, tree, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              property alias label: button.text
              property alias extra: extra.visible
              Button { id: button; text: "Play" }
              CheckBox { id: box; text: "Shuffle" }
              Button { id: extra; text: "More"; visible: false }
            }
            """);
        using var _ = shell;
        var button = tree.Root.Children[0];
        var box = tree.Root.Children[1];
        Assert.Empty(tree.Refresh());

        root.WriteProperty("label", "Pause");
        box.DoToggle();
        var changes = tree.Refresh();
        Assert.Contains(new AccessibleChange(AccessibleChangeKind.Property, button, AccessibleProperty.Name, "Play", "Pause"), changes);
        Assert.Contains(new AccessibleChange(AccessibleChangeKind.Property, box, AccessibleProperty.ToggleState, ToggleState.Off, ToggleState.On), changes);

        root.WriteProperty("extra", true);
        changes = tree.Refresh();
        Assert.Contains(changes, c => c.Kind == AccessibleChangeKind.Children && c.Node == tree.Root);
        var more = tree.Root.Children[2];
        Assert.Contains(new AccessibleChange(AccessibleChangeKind.Added, more), changes);

        root.WriteProperty("extra", false);
        changes = tree.Refresh();
        Assert.Contains(new AccessibleChange(AccessibleChangeKind.Removed, more), changes);
        Assert.False(more.IsAlive);
        Assert.Equal(2, tree.Root.Children.Count);
    }

    [Fact]
    public void Bounds_hit_testing_and_offscreen_follow_the_items()
    {
        var (_, _, tree, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              Button { x: 10; y: 20; width: 100; height: 30; text: "A" }
              Flickable {
                y: 100; width: 200; height: 50; clip: true
                contentHeight: 200
                Button { y: 160; width: 100; height: 30; text: "Below" }
              }
            }
            """);
        using var _ = shell;
        var a = tree.Root.Children[0];
        Assert.Equal(new QRect(10, 20, 100, 30), a.Bounds);
        Assert.Same(a, tree.NodeAt(50, 30));
        Assert.Same(tree.Root, tree.NodeAt(300, 300));

        var pane = tree.Root.Children[1];
        Assert.Equal(Role.Pane, pane.Role);
        var below = Assert.Single(pane.Children);
        Assert.True(below.IsOffscreen);
        Assert.False(a.IsOffscreen);
        Assert.Equal(25, pane.Scroll!.VerticalViewSize);
        Assert.True(pane.Scroll.VerticallyScrollable);
        Assert.False(pane.Scroll.HorizontallyScrollable);

        pane.SetScrollPercent(-1, 100);
        Assert.Equal(100, pane.Scroll!.VerticalPercent);
        Assert.False(below.IsOffscreen);
    }

    [Fact]
    public void A_window_is_named_by_accessible_or_its_title()
    {
        var (_, scene, _, shell) = Show("""
            import QtQuick
            Item {}
            """);
        using var _ = shell;
        var window = new Quickshell.FloatingWindow { Title = "Settings" };
        var tree = new AccessibleTree(new Scene(window));
        Assert.Equal(Role.Window, tree.Root.Role);
        Assert.Equal("Settings", tree.Root.Name);
        ((AccessibleAttached)window.GetAttached(typeof(AccessibleAttached), o => new AccessibleAttached(o))).Name = "Preferences";
        Assert.Equal("Preferences", tree.Root.Name);
        Assert.Equal("", new AccessibleTree(scene).Root.Name);
    }
}
