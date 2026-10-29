using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

public class RepeaterModelUpdateTests
{
    private const string Rows = """
        ListModel {
          id: rows
          ListElement { name: "a"; cost: 1 }
          ListElement { name: "b"; cost: 2 }
        }
        """;

    private static (Item Root, TestShell Shell) Load(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, shell);
    }

    private static List<Text> Texts(Item root) => root.ChildItems.OfType<Text>().ToList();

    [Fact]
    public void Append_and_setProperty_keep_every_delegate()
    {
        var (root, shell) = Load($$"""
            import QtQuick
            Column {
              {{Rows}}
              Repeater {
                model: rows
                Text { required property int cost; text: model.name + "=" + cost }
              }
            }
            """);
        using var _ = shell;
        var before = Texts(root);
        Assert.Equal(["a=1", "b=2"], before.Select(t => t.Text_));

        shell.Eval(root, "rows.append({ name: 'c', cost: 3 })");
        var appended = Texts(root);
        Assert.Same(before[0], appended[0]);
        Assert.Same(before[1], appended[1]);

        shell.Eval(root, "rows.setProperty(1, 'name', 'B')");
        shell.Eval(root, "rows.setProperty(1, 'cost', 5)");
        shell.Eval(root, "rows.set(0, { name: 'A' })");
        Assert.Equal(appended, Texts(root));
        Assert.Equal(["A=1", "B=5", "c=3"], Texts(root).Select(t => t.Text_));
    }

    [Fact]
    public void A_thousand_appends_make_one_delegate_per_row_in_order()
    {
        var (root, shell) = Load("""
            import QtQuick
            Column {
              ListModel { id: rows }
              Repeater {
                id: repeater
                model: rows
                Text { required property int index; text: model.name + index }
              }
              Item { objectName: "after" }
              function fill(n) { for (let i = 0; i < n; i++) rows.append({ name: "r" + i }) }
            }
            """);
        using var _ = shell;
        var repeater = root.ChildItems.OfType<Repeater>().Single();
        var childrenChanges = 0;
        root.Children.Changed += () => childrenChanges++;

        shell.Eval(root, "fill(1000)");

        Assert.Equal(1000, repeater.Count);
        var texts = Texts(root);
        Assert.Equal(Enumerable.Range(0, 1000).Select(i => $"r{i}{i}"), texts.Select(t => t.Text_));
        Assert.Equal(texts, Enumerable.Range(0, 1000).Select(i => repeater.ItemAt(i)));
        // The repeater, its delegates in model order, then the item after it.
        Assert.Same(repeater, root.ChildItems[0]);
        Assert.Equal("after", root.ChildItems[^1].ObjectName);
        // Each append adds its delegate and moves it before "after", nothing more.
        Assert.Equal(2000, childrenChanges);
    }

    [Fact]
    public void A_move_reorders_the_children_once()
    {
        var (root, shell) = Load("""
            import QtQuick
            Column {
              ListModel { id: rows }
              Repeater {
                model: rows
                Text { text: model.name }
              }
              Item { objectName: "after" }
              Component.onCompleted: for (let i = 0; i < 20; i++) rows.append({ name: "r" + i })
            }
            """);
        using var _ = shell;
        var before = Texts(root);
        var childrenChanges = 0;
        root.Children.Changed += () => childrenChanges++;

        shell.Eval(root, "rows.move(0, 19, 1)");

        Assert.Equal(1, childrenChanges);
        Assert.Equal(before.Skip(1).Append(before[0]), Texts(root));
        Assert.Equal("after", root.ChildItems[^1].ObjectName);

        childrenChanges = 0;
        shell.Eval(root, "rows.setProperty(3, 'name', 'x')");
        Assert.Equal(0, childrenChanges);
        Assert.Equal("x", Texts(root)[3].Text_);
    }

    [Fact]
    public void A_list_view_delegate_follows_setProperty()
    {
        var (root, shell) = Load($$"""
            import QtQuick
            ListView {
              width: 100; height: 100
              {{Rows}}
              model: rows
              delegate: Text { required property int cost; text: model.name + "=" + cost }
            }
            """);
        using var _ = shell;
        var view = (ListView)root;
        var first = view.ItemAtIndex(0);

        shell.Eval(root, "rows.setProperty(0, 'cost', 7)");

        Assert.Same(first, view.ItemAtIndex(0));
        Assert.Equal("a=7", ((Text)first!).Text_);
    }

    [Fact]
    public void A_combo_box_shows_a_row_changed_by_setProperty()
    {
        var (root, shell) = Load($$"""
            import QtQuick
            import QtQuick.Controls
            ComboBox {
              {{Rows}}
              model: rows
              textRole: "name"
            }
            """);
        using var _ = shell;
        var box = (ComboBox)root;
        Assert.Equal("a", box.DisplayText);

        shell.Eval(root, "rows.setProperty(0, 'name', 'z')");

        Assert.Equal("z", box.DisplayText);
    }
}
