namespace Winshell.Qml.Tests;

public class QmlParserTests
{
    [Fact]
    public void Parses_imports_pragmas_and_the_root_object()
    {
        var doc = QmlParser.Parse("""
            pragma Singleton
            pragma ComponentBehavior: Bound
            import QtQuick 6.0
            import Winshell as WS
            import "myjs.js" as MyJs
            import "./widgets"
            import config.services

            Item {}
            """);

        Assert.True(doc.IsSingleton);
        Assert.Equal("Bound", doc.Pragma("ComponentBehavior"));
        Assert.Collection(doc.Imports,
            i => { Assert.Equal(ImportKind.Module, i.Kind); Assert.Equal("QtQuick", i.Uri); Assert.Equal("6.0", i.Version); },
            i => { Assert.Equal("Winshell", i.Uri); Assert.Equal("WS", i.Qualifier); },
            i => { Assert.Equal(ImportKind.Script, i.Kind); Assert.Equal("MyJs", i.Qualifier); },
            i => { Assert.Equal(ImportKind.Directory, i.Kind); Assert.Equal("./widgets", i.Uri); },
            i => Assert.Equal("config.services", i.Uri));
        Assert.Equal("Item", doc.Root.TypeName);
    }

    [Fact]
    public void Parses_every_member_kind()
    {
        var doc = QmlParser.Parse("""
            import QtQuick
            Item {
              id: root
              property int myProp: 5;
              required property var modelData
              readonly property string bar: "hi!"
              default property list<Item> things
              property alias label: text.text
              width: 100
              height: width
              prop: {
                const foo = 5;
                foo * 2
              }
              objProp: QtObject { }
              AnotherObject { }
              signal foo(bar: int)
              signal old(int a, string b)
              signal bare
              onSignal: console.log("received signal!")
              onWidthChanged: console.log(`width is now ${width}!`)
              Component.onCompleted: MyJs.myfunction()
              anchors { top: parent.top; left: parent.left }
              anchors.margins: 4
              function dub(x: int): int {
                return x * 2
              }
              component MyComponent: Text { color: "red" }
              enum Mode { Off, On = 5, Auto }
              Behavior on width { NumberAnimation { duration: 100 } }
              children: [ Rectangle {}, Rectangle {} ]
              list: [1, 2, 3]
            }
            """);

        var m = doc.Root.Members;
        Assert.Equal("root", doc.Root.Id);
        var myProp = Assert.IsType<QmlPropertyDeclaration>(m[1]);
        Assert.Equal(("myProp", "int"), (myProp.Name, myProp.Type));
        Assert.Equal("5", Assert.IsType<QmlScriptValue>(myProp.Value).Code);
        Assert.True(Assert.IsType<QmlPropertyDeclaration>(m[2]).IsRequired);
        Assert.True(Assert.IsType<QmlPropertyDeclaration>(m[3]).IsReadonly);
        var things = Assert.IsType<QmlPropertyDeclaration>(m[4]);
        Assert.True(things.IsDefault);
        Assert.Equal("list<Item>", things.Type);
        Assert.True(Assert.IsType<QmlPropertyDeclaration>(m[5]).IsAlias);
        Assert.Equal("100", Assert.IsType<QmlScriptValue>(Assert.IsType<QmlAssignment>(m[6]).Value).Code);
        var block = Assert.IsType<QmlScriptValue>(Assert.IsType<QmlAssignment>(m[8]).Value);
        Assert.True(block.IsBlock);
        Assert.Contains("foo * 2", block.Code);
        Assert.Equal("QtObject", Assert.IsType<QmlObjectValue>(Assert.IsType<QmlAssignment>(m[9]).Value).Object.TypeName);
        Assert.Equal("AnotherObject", Assert.IsType<QmlChildObject>(m[10]).Object.TypeName);
        Assert.Equal("bar", Assert.IsType<QmlSignalDeclaration>(m[11]).Parameters.Single().Name);
        Assert.Equal(["a", "b"], Assert.IsType<QmlSignalDeclaration>(m[12]).Parameters.Select(p => p.Name));
        Assert.Empty(Assert.IsType<QmlSignalDeclaration>(m[13]).Parameters);
        Assert.Equal("console.log(`width is now ${width}!`)", Assert.IsType<QmlScriptValue>(Assert.IsType<QmlAssignment>(m[15]).Value).Code);
        Assert.Equal("Component.onCompleted", Assert.IsType<QmlAssignment>(m[16]).Name);
        Assert.Equal(2, Assert.IsType<QmlGroupedBlock>(m[17]).Members.Count);
        Assert.Equal("anchors.margins", Assert.IsType<QmlAssignment>(m[18]).Name);
        var dub = Assert.IsType<QmlFunctionDeclaration>(m[19]);
        Assert.Equal("x", dub.Parameters.Single().Name);
        Assert.Contains("return x * 2", dub.Body);
        Assert.Equal("MyComponent", Assert.IsType<QmlInlineComponent>(m[20]).Name);
        Assert.Equal([new("Off", 0), new("On", 5), new KeyValuePair<string, int>("Auto", 6)], Assert.IsType<QmlEnumDeclaration>(m[21]).Values);
        Assert.Equal("width", Assert.IsType<QmlOnAssignment>(m[22]).Property);
        Assert.Equal(2, Assert.IsType<QmlListValue>(Assert.IsType<QmlAssignment>(m[23]).Value).Objects.Count);
        Assert.Equal("[1, 2, 3]", Assert.IsType<QmlScriptValue>(Assert.IsType<QmlAssignment>(m[24]).Value).Code);
    }

    [Theory]
    [InlineData("a: 1 +\n 2\nb: 3", "1 +\n 2")]
    [InlineData("a: foo\n  .bar()\nb: 3", "foo\n  .bar()")]
    [InlineData("a: x ? 1\n : 2\nb: 3", "x ? 1\n : 2")]
    [InlineData("a: x ?? 2\nb: 3", "x ?? 2")]
    [InlineData("a: x?.y\nb: 3", "x?.y")]
    [InlineData("a: f(1,\n 2)\nb: 3", "f(1,\n 2)")]
    [InlineData("a: (m) => {\n  go(m)\n}\nb: 3", "(m) => {\n  go(m)\n}")]
    [InlineData("a: \"}\" // c\nb: 3", "\"}\" // c")]
    [InlineData("a: /x\\/}/.test(s)\nb: 3", "/x\\/}/.test(s)")]
    [InlineData("a: 5; b: 3", "5")]
    public void Finds_where_an_expression_ends(string body, string expected)
    {
        var doc = QmlParser.Parse("Item {\n" + body + "\n}");
        var a = Assert.IsType<QmlAssignment>(doc.Root.Members[0]);
        Assert.Equal(expected, Assert.IsType<QmlScriptValue>(a.Value).Code.Trim());
        Assert.Equal("b", Assert.IsType<QmlAssignment>(doc.Root.Members[1]).Name);
    }

    [Theory]
    [InlineData("a: x++\nb: 3", "x++")]
    [InlineData("a: x--\nb: 3", "x--")]
    [InlineData("a: x++ +\n 1\nb: 3", "x++ +\n 1")]
    [InlineData("a: x +\n +y\nb: 3", "x +\n +y")]
    public void A_postfix_update_ends_the_expression(string body, string expected)
    {
        var doc = QmlParser.Parse("Item {\n" + body + "\n}");
        var a = Assert.IsType<QmlAssignment>(doc.Root.Members[0]);
        Assert.Equal(expected, Assert.IsType<QmlScriptValue>(a.Value).Code.Trim());
        Assert.Equal("b", Assert.IsType<QmlAssignment>(doc.Root.Members[1]).Name);
    }

    [Fact]
    public void A_one_line_object_ends_at_its_brace()
    {
        var doc = QmlParser.Parse("Item { Text { text: \"a\"; color: c } Rectangle { width: 5 } }");
        Assert.Equal(2, doc.Root.Members.Count);
        var text = Assert.IsType<QmlChildObject>(doc.Root.Members[0]).Object;
        Assert.Equal("c", Assert.IsType<QmlScriptValue>(Assert.IsType<QmlAssignment>(text.Members[1]).Value).Code);
    }

    [Fact]
    public void Keywords_can_be_property_names()
    {
        var doc = QmlParser.Parse("Item { property: 5\n signal: 1\n default: 2\n required: true }");
        Assert.Equal(["property", "signal", "default", "required"], doc.Root.Members.Cast<QmlAssignment>().Select(a => a.Name));
    }

    [Theory]
    [InlineData("Item { enum E { A = B } }")]
    [InlineData("Item { enum E { A = 0xFFFFFFFF } }")]
    [InlineData("Item { enum E { A = } }")]
    [InlineData("Item { a: /unterminated")]
    [InlineData("Item {\n  a: /foo\\\n}")]
    [InlineData("Item { a: \"open\\")]
    public void A_bad_literal_is_a_syntax_error(string source)
    {
        var ex = Assert.Throws<QmlSyntaxException>(() => QmlParser.Parse(source, "shell.qml"));
        Assert.False(string.IsNullOrWhiteSpace(ex.Reason));
        Assert.Contains("shell.qml", ex.Message);
    }

    [Fact]
    public void A_regular_expression_stays_in_the_expression()
    {
        var doc = QmlParser.Parse("Item {\n  a: /foo/i\n  b: 1\n}");
        var a = Assert.IsType<QmlAssignment>(doc.Root.Members[0]);
        Assert.Equal("/foo/i", Assert.IsType<QmlScriptValue>(a.Value).Code.Trim());
        Assert.Equal("b", Assert.IsType<QmlAssignment>(doc.Root.Members[1]).Name);
    }

    [Fact]
    public void Required_name_marks_an_existing_property()
    {
        var doc = QmlParser.Parse("Item {\n  required label\n  required property int value: 1\n}");
        Assert.Equal("label", Assert.IsType<QmlRequiredMember>(doc.Root.Members[0]).Name);
        var declared = Assert.IsType<QmlPropertyDeclaration>(doc.Root.Members[1]);
        Assert.True(declared.IsRequired);
        Assert.Equal("value", declared.Name);
    }

    [Fact]
    public void Reports_where_a_syntax_error_is()
    {
        var ex = Assert.Throws<QmlSyntaxException>(() => QmlParser.Parse("Item {\n  width: 5\n  height 3\n}", "shell.qml"));
        Assert.Equal(3, ex.Location.Line);
    }

    [Theory]
    [InlineData(")", "Unexpected ')'")]
    [InlineData("]", "Unexpected ']'")]
    public void A_stray_closer_is_a_syntax_error(string closer, string reason)
    {
        var ex = Assert.Throws<QmlSyntaxException>(() => QmlParser.Parse($"Item {{\n  a: foo{closer}\n  b: 1\n}}", "shell.qml"));
        Assert.Equal(reason, ex.Reason);
        Assert.Equal(2, ex.Location.Line);
    }

    [Fact]
    public void Deep_object_and_template_nesting_is_a_syntax_error()
    {
        var objects = string.Concat(Enumerable.Repeat("Item { ", 300)) + string.Concat(Enumerable.Repeat("}", 300));
        var objectsError = Assert.Throws<QmlSyntaxException>(() => QmlParser.Parse(objects, "shell.qml"));
        Assert.Equal("Nesting is too deep", objectsError.Reason);

        var inner = "1";
        for (var i = 0; i < 300; i++)
            inner = "`${" + inner + "}`";
        var templateError = Assert.Throws<QmlSyntaxException>(() => QmlParser.Parse("Item { a: " + inner + " }", "shell.qml"));
        Assert.Equal("Nesting is too deep", templateError.Reason);
    }

    [Fact]
    public void Locations_point_at_the_code()
    {
        var doc = QmlParser.Parse("Item {\n  x: 1\n  y: {\n    2\n  }\n}");
        var y = Assert.IsType<QmlScriptValue>(Assert.IsType<QmlAssignment>(doc.Root.Members[1]).Value);
        Assert.Equal(3, y.Location.Line);
        Assert.Equal(3, doc.Root.Members[1].Location.Line);
    }

    [Fact]
    public void A_crlf_backslash_continues_a_string()
    {
        var doc = QmlParser.Parse("Item {\r\n  a: \"foo\\\r\nbar\"\r\n  b: 3\r\n}");
        var a = Assert.IsType<QmlAssignment>(doc.Root.Members[0]);
        Assert.Contains("foo", Assert.IsType<QmlScriptValue>(a.Value).Code);
        Assert.Contains("bar", Assert.IsType<QmlScriptValue>(a.Value).Code);
        Assert.Equal("b", Assert.IsType<QmlAssignment>(doc.Root.Members[1]).Name);

        var imported = QmlParser.Parse("import \"widgets\\\r\nextra.js\" as Extra\nItem {}");
        Assert.Equal("widgetsextra.js", imported.Imports[0].Uri);
        Assert.Equal("Extra", imported.Imports[0].Qualifier);
    }

    [Fact]
    public void The_root_location_is_the_type_name()
    {
        var doc = QmlParser.Parse("\n  Item {}", "shell.qml");
        Assert.Equal(2, doc.Root.Location.Line);
        Assert.Equal(3, doc.Root.Location.Column);
        Assert.Equal("shell.qml", doc.Root.Location.File);
    }
}
