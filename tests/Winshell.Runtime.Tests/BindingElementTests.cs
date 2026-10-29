namespace Winshell.Runtime.Tests;

public class BindingElementTests
{
    [Fact]
    public void Binding_beats_the_target_binding_when_its_input_changes()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property real input: 1
              property QtObject t: QtObject { property real v: input * 10 }
              Binding { target: t; property: "v"; value: 5 }
            }
            """, out var shell);
        using var _ = shell;
        var t = (QObject)root.ReadProperty("t")!;
        Assert.Equal(5.0, t.ReadProperty("v"));
        root.WriteProperty("input", 2);
        Assert.Equal(5.0, t.ReadProperty("v"));
    }

    [Fact]
    public void When_false_restores_the_live_binding()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property real input: 1
              property bool on: true
              property QtObject t: QtObject { property real v: input * 10 }
              Binding { target: t; property: "v"; value: 5; when: on }
            }
            """, out var shell);
        using var _ = shell;
        var t = (QObject)root.ReadProperty("t")!;
        root.WriteProperty("input", 2);
        Assert.Equal(5.0, t.ReadProperty("v"));
        root.WriteProperty("on", false);
        Assert.Equal(20.0, t.ReadProperty("v"));
        root.WriteProperty("input", 3);
        Assert.Equal(30.0, t.ReadProperty("v"));
        root.WriteProperty("on", true);
        Assert.Equal(5.0, t.ReadProperty("v"));
    }

    [Fact]
    public void When_false_restores_a_plain_value()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property bool on: true
              property QtObject t: QtObject { property real v: 7 }
              Binding { target: t; property: "v"; value: 5; when: on }
            }
            """, out var shell);
        using var _ = shell;
        var t = (QObject)root.ReadProperty("t")!;
        Assert.Equal(5.0, t.ReadProperty("v"));
        root.WriteProperty("on", false);
        Assert.Equal(7.0, t.ReadProperty("v"));
    }

    [Fact]
    public void Retargeting_restores_the_old_target()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property real input: 1
              property bool useA: true
              property QtObject a: QtObject { property real v: input * 10 }
              property QtObject b: QtObject { property real v: input * 100 }
              Binding { target: useA ? a : b; property: "v"; value: 5 }
            }
            """, out var shell);
        using var _ = shell;
        var a = (QObject)root.ReadProperty("a")!;
        var b = (QObject)root.ReadProperty("b")!;
        Assert.Equal(5.0, a.ReadProperty("v"));
        Assert.Equal(100.0, b.ReadProperty("v"));
        root.WriteProperty("useA", false);
        Assert.Equal(10.0, a.ReadProperty("v"));
        Assert.Equal(5.0, b.ReadProperty("v"));
        root.WriteProperty("input", 2);
        Assert.Equal(20.0, a.ReadProperty("v"));
        Assert.Equal(5.0, b.ReadProperty("v"));
    }

    [Fact]
    public void Changing_the_property_restores_the_old_one()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property real input: 1
              property string prop: "v"
              property QtObject t: QtObject { property real v: input * 10; property real w: input * 100 }
              Binding { target: t; property: prop; value: 5 }
            }
            """, out var shell);
        using var _ = shell;
        var t = (QObject)root.ReadProperty("t")!;
        root.WriteProperty("prop", "w");
        Assert.Equal(10.0, t.ReadProperty("v"));
        Assert.Equal(5.0, t.ReadProperty("w"));
    }

    [Fact]
    public void Destroying_the_Binding_restores_the_target()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property real input: 1
              property QtObject t: QtObject { property real v: input * 10 }
              property QtObject element: Binding { target: t; property: "v"; value: 5 }
            }
            """, out var shell);
        using var _ = shell;
        var t = (QObject)root.ReadProperty("t")!;
        Assert.Equal(5.0, t.ReadProperty("v"));
        ((QObject)root.ReadProperty("element")!).Destroy();
        Assert.Equal(10.0, t.ReadProperty("v"));
        root.WriteProperty("input", 2);
        Assert.Equal(20.0, t.ReadProperty("v"));
    }

    [Fact]
    public void A_bound_value_stays_live()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property real input: 1
              property real source: 3
              property QtObject t: QtObject { property real v: input * 10 }
              Binding { target: t; property: "v"; value: source * 2 }
            }
            """, out var shell);
        using var _ = shell;
        var t = (QObject)root.ReadProperty("t")!;
        Assert.Equal(6.0, t.ReadProperty("v"));
        root.WriteProperty("source", 4);
        Assert.Equal(8.0, t.ReadProperty("v"));
        root.WriteProperty("input", 2);
        Assert.Equal(8.0, t.ReadProperty("v"));
    }

    [Fact]
    public void RestoreNone_leaves_the_value_and_drops_the_binding()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property real input: 1
              property bool on: true
              property QtObject t: QtObject { property real v: input * 10 }
              Binding { target: t; property: "v"; value: 5; when: on; restoreMode: Binding.RestoreNone }
            }
            """, out var shell);
        using var _ = shell;
        var t = (QObject)root.ReadProperty("t")!;
        root.WriteProperty("on", false);
        Assert.Equal(5.0, t.ReadProperty("v"));
        root.WriteProperty("input", 2);
        Assert.Equal(5.0, t.ReadProperty("v"));
    }
}
