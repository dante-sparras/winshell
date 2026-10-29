namespace Winshell.Runtime.Tests;

public class EngineTests
{
    [Fact]
    public void Bindings_follow_the_properties_they_read()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              id: root
              property int clicks: 0
              property string label: `clicks: ${clicks}`
              property int doubled: { const x = clicks; x * 2 }
              property int viaFunction: twice()
              function twice(): int { return clicks * 2 }
            }
            """, out var shell);
        using var _ = shell;
        Assert.Equal("clicks: 0", root.ReadProperty("label"));
        root.WriteProperty("clicks", 3);
        Assert.Equal("clicks: 3", root.ReadProperty("label"));
        Assert.Equal(6, root.ReadProperty("doubled"));
        Assert.Equal(6, root.ReadProperty("viaFunction"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_script_assignment_breaks_the_binding_and_Qt_binding_makes_a_new_one()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              property int a: 1
              property int b: a + 1
              function breakIt() { b = 10 }
              function rebind() { b = Qt.binding(() => a * 100) }
            }
            """, out var shell);
        using var _ = shell;
        shell.Eval(root, "breakIt()");
        root.WriteProperty("a", 5);
        Assert.Equal(10, root.ReadProperty("b"));
        shell.Eval(root, "rebind()");
        Assert.Equal(500, root.ReadProperty("b"));
        root.WriteProperty("a", 2);
        Assert.Equal(200, root.ReadProperty("b"));
    }

    [Fact]
    public void Ids_signals_handlers_and_Component_onCompleted()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              id: root
              property var log: []
              property QtObject child: QtObject {
                id: inner
                signal ping(value: int)
                onPing: value => root.log.push("arrow " + value)
              }
              property Connections c: Connections {
                target: inner
                function onPing(value) { root.log.push("connections " + value) }
              }
              Component.onCompleted: {
                inner.ping.connect(v => log.push("connect " + v))
                inner.ping(7)
              }
              onLogChanged: {}
            }
            """, out var shell);
        using var _ = shell;
        var log = (List<object?>)shell.Eval(root, "log")!;
        Assert.Equal(["arrow 7", "connections 7", "connect 7"], log.Cast<string>());
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Property_change_handlers_run_with_the_new_value()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              property int width: 1
              property string seen: ""
              onWidthChanged: seen = `width is now ${width}!`
            }
            """, out var shell);
        using var _ = shell;
        root.WriteProperty("width", 4);
        Assert.Equal("width is now 4!", root.ReadProperty("seen"));
    }

    [Fact]
    public void Timer_triggers_at_its_interval()
    {
        var root = TestShell.Run("""
            import QtQuick
            QtObject {
              property int ticks: 0
              property Timer t: Timer { interval: 1000; running: true; repeat: true; onTriggered: ticks++ }
            }
            """, out var shell);
        using var _ = shell;
        shell.Loop.Advance(TimeSpan.FromMilliseconds(3500));
        Assert.Equal(3, root.ReadProperty("ticks"));
    }

    [Fact]
    public void File_types_singletons_and_inline_components()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                QtObject {
                  id: root
                  property real textPointSize: 10
                  property ClockWidget clock: ClockWidget { time: Time.time }
                  property MyText inline: MyText { }
                  component MyText: QtObject {
                    property real size: root.textPointSize
                  }
                }
                """),
            ("ClockWidget.qml", """
                import QtQml
                QtObject {
                  required property string time
                  property string text: "it is " + time
                }
                """),
            ("Time.qml", """
                pragma Singleton
                import QtQml
                QtObject {
                  property string time: "noon"
                }
                """));
        var root = shell.Load();
        var clock = (QObject)root.ReadProperty("clock")!;
        Assert.Equal("it is noon", clock.ReadProperty("text"));
        var inline = (QObject)root.ReadProperty("inline")!;
        root.WriteProperty("textPointSize", 12);
        Assert.Equal(12.0, inline.ReadProperty("size"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Required_properties_that_are_not_set_are_reported()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nQtObject { property Thing t: Thing {} }"),
            ("Thing.qml", "import QtQml\nQtObject { required property string name }"));
        shell.Load();
        Assert.Contains(shell.Messages, m => m.Contains("Required property name was not initialized", StringComparison.Ordinal));
    }

    [Fact]
    public void Aliases_read_and_write_their_target()
    {
        using var shell = new TestShell(
            ("shell.qml", """
                import QtQml
                QtObject {
                  property Labeled l: Labeled { label: "hi" }
                }
                """),
            ("Labeled.qml", """
                import QtQml
                QtObject {
                  property alias label: inner.text
                  property QtObject holder: QtObject { id: inner; property string text: "" }
                  property string shown: inner.text
                }
                """));
        var root = shell.Load();
        var labeled = (QObject)root.ReadProperty("l")!;
        Assert.Equal("hi", labeled.ReadProperty("label"));
        Assert.Equal("hi", labeled.ReadProperty("shown"));
    }

    [Fact]
    public void Script_errors_are_reported_with_where_they_happened()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              property string t: missing.text
            }
            """, out var shell);
        using var _ = shell;
        Assert.Contains(shell.Messages, m => m.Contains("shell.qml[3:", StringComparison.Ordinal) && m.Contains("ReferenceError", StringComparison.Ordinal));
        Assert.NotNull(root);
    }

    [Fact]
    public void Javascript_imports_expose_their_functions()
    {
        using var shell = new TestShell(
            ("shell.qml", "import QtQml\nimport \"util.js\" as Util\nQtObject { property int v: Util.add(2, 3) }"),
            ("util.js", ".pragma library\nfunction add(a, b) { return a + b }"));
        var root = shell.Load();
        Assert.Equal(5, root.ReadProperty("v"));
    }

    [Fact]
    public void Qt_helpers_format_dates_and_colors()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              property string d: Qt.formatDateTime(new Date(2026, 9, 28, 14, 5, 9), "ddd MMM d hh:mm:ss AP yyyy")
              property color c: Qt.rgba(1, 0, 0, 1)
              property string cs: c.toString()
              property color named: "steelblue"
            }
            """, out var shell);
        using var _ = shell;
        Assert.Equal("Wed Oct 28 02:05:09 PM 2026", root.ReadProperty("d"));
        Assert.Equal("#ff0000", root.ReadProperty("cs"));
        Assert.Equal(QColor.Parse("#4682b4"), root.ReadProperty("named"));
    }

    [Fact]
    public void Enum_types_name_their_values_with_toString()
    {
        var registry = StandardModules.Create();
        registry.Module("Test.Enums").Add(new TypeEntry
        {
            Name = "Fruit",
            Module = "Test.Enums",
            ClrType = typeof(object),
            Creatable = false,
            Enums = new Dictionary<string, int> { ["Apple"] = 0, ["Pear"] = 1 },
            EnumToString = v => v == 1 ? "A pear" : "An apple",
        });
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            import Test.Enums
            QtObject {
              property int pear: Fruit.Pear
              property string name: Fruit.toString(Fruit.Pear)
            }
            """));
        using var engine = new QmlEngine(registry, shell.Loop, shell.Log, shell.Directory);
        var root = engine.CreateRoot(Path.Combine(shell.Directory, "shell.qml"));
        shell.Loop.Process();
        Assert.Equal(1, root.ReadProperty("pear"));
        Assert.Equal("A pear", root.ReadProperty("name"));
    }

    [Fact]
    public void Functions_take_defaults_rest_and_destructured_parameters()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              property string joined: join(1)
              property int sum: add({ a: 2, b: 3 }, [4])
              function join(a, b = 2, ...rest): string { return [a, b, rest.length].join() }
              function add({ a, b }, [c]) { return a + b + c }
            }
            """, out var shell);
        using var _ = shell;
        Assert.Equal("1,2,0", root.ReadProperty("joined"));
        Assert.Equal(9, root.ReadProperty("sum"));
        Assert.Equal("5,6,2", shell.Eval(root, "join(5, 6, 7, 8)"));
        Assert.Empty(shell.Messages);
    }
}
