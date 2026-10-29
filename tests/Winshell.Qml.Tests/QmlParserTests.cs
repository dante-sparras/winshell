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

    [Fact]
    public void Function_parameters_keep_their_javascript()
    {
        var doc = QmlParser.Parse("""
            Item {
              function f(a, b = g(1, 2), ...rest) {}
              function g({ x, y: [z] }, [p, q] = [1, 2]) {}
              function h(n: int = 1, ...more: list) {}
            }
            """);

        var functions = doc.Root.Members.Cast<QmlFunctionDeclaration>().ToList();
        Assert.Equal(["a", "b = g(1, 2)", "...rest"], functions[0].Parameters.Select(p => p.Name));
        Assert.Equal(["{ x, y: [z] }", "[p, q] = [1, 2]"], functions[1].Parameters.Select(p => p.Name));
        Assert.Equal([new("n = 1", "int"), new QmlParameter("...more", "list")], functions[2].Parameters);
    }

    [Theory]
    [InlineData("Item { function f(...) {} }")]
    [InlineData("Item { function f({ a, b) {} }")]
    [InlineData("Item { function f(a: ) {} }")]
    public void A_bad_function_parameter_is_a_syntax_error(string source)
    {
        Assert.Throws<QmlSyntaxException>(() => QmlParser.Parse(source));
    }

    [Theory]
    [InlineData("", "Expected a root object")]
    [InlineData("import QtQuick\n", "Expected a root object")]
    [InlineData("Item {} Item {}", "after the root object")]
    [InlineData("Item {\n  width: 5\n", "Unterminated object 'Item'")]
    [InlineData("Item { anchors { fill: parent }", "Unterminated object 'Item'")]
    [InlineData("Item { anchors { fill: parent ", "Unterminated group 'anchors'")]
    [InlineData("Item { anchors { fill } }", "Expected ':' after 'fill'")]
    [InlineData("Item { width: }", "Expected a value")]
    [InlineData("Item { width: 5 /* open", "Unterminated comment")]
    [InlineData("Item { a: `open ${x}", "Unterminated template literal")]
    [InlineData("Item { a: \"open\nb: 1 }", "Unterminated string")]
    [InlineData("Item { a: { f(] } }", "Mismatched ']'")]
    [InlineData("Item { a: { f( }", "Mismatched '}'")]
    [InlineData("Item { function f() { return 1 }", "Unterminated object 'Item'")]
    [InlineData("Item { function f() { return 1 ", "Expected '}'")]
    [InlineData("Item { readonly default x: 1 }", "Expected 'property'")]
    [InlineData("Item { property int }", "Expected an identifier, found '}'")]
    [InlineData("Item { property list<int x }", "Expected '>'")]
    [InlineData("Item { enum E { 1 } }", "Expected an identifier, found '1'")]
    [InlineData("Item { component C Text {} }", "Expected ':'")]
    [InlineData("Item { Text }", "Unexpected '}' after 'Text'")]
    [InlineData("Item { width", "Unexpected 'end of file' after 'width'")]
    [InlineData("import \"x.js\"\nItem {}", "requires a qualifier")]
    [InlineData("import QtQuick as qq\nItem {}", "must start with an uppercase letter")]
    [InlineData("Item", "Expected '{' before the end of file")]
    public void A_syntax_error_says_what_is_wrong(string source, string reason)
    {
        var ex = Assert.Throws<QmlSyntaxException>(() => QmlParser.Parse(source, "shell.qml"));
        Assert.Contains(reason, ex.Reason, StringComparison.Ordinal);
        Assert.StartsWith("shell.qml[", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_syntax_error_points_at_its_line_and_column()
    {
        var ex = Assert.Throws<QmlSyntaxException>(() => QmlParser.Parse("Item {\n  Text {\n    text: \"a\n  }\n}"));
        Assert.Equal((3, 13), (ex.Location.Line, ex.Location.Column));
    }

    private static readonly string[] FuzzSeeds =
    [
        """
        pragma Singleton
        pragma ComponentBehavior: Bound
        import QtQuick 6.0
        import "util.js" as Util
        import "./widgets"

        Item {
          id: root
          required property var modelData
          readonly property string label: "a\"b" + 'c\'d'
          default property list<Item> things
          property alias text: inner.text
          signal moved(x: real, y: real)
          signal old(int a, string b)
          signal bare
          enum Mode { Off, On = 5, Auto = 0x10, Down = -1 }
          anchors { top: parent.top; margins: 4 }
          Layout.fillWidth: true
          Behavior on width { NumberAnimation { duration: 100 } }
          component Label: Text { color: "red" }
          children: [ Rectangle {}, Rectangle { width: 2 } ]
          Text { id: inner; text: `n = ${Util.count(root, { a: [1, 2] })}` }
        }
        """,
        """
        import QtQuick
        Item {
          property var items: [{ a: 1 }, { b: [2, 3] }]
          property bool matches: /[/}]+\/x/g.test(label) && a < b || !c
          property int n: x ? y
            : z ?? 0
          property int sum: 1 +
            2
          onClicked: (mouse) => { count++; if (mouse.x > 0) { go(/* } */ 1) } }
          onPressed: count--
          property real chained: foo
            .bar(1, `${`nested ${x}`}`)
          function f(a: int, b = { k: [1] }): int {
            // a comment with a } brace
            const re = /\)/
            return a / 2 + b.k[0]
          }
          Component.onCompleted: { console.log("done"); }
        }
        """,
    ];

    [Fact]
    public void Broken_documents_fail_only_with_syntax_errors()
    {
        var random = new Random(20261029);
        foreach (var seed in FuzzSeeds)
        {
            QmlParser.Parse(seed);
            for (var i = 0; i <= seed.Length; i++)
                ParsesOrFailsCleanly(seed[..i]);
            for (var i = 0; i < seed.Length; i++)
            {
                ParsesOrFailsCleanly(seed.Remove(i, 1));
                ParsesOrFailsCleanly(seed.Insert(i, seed[i].ToString()));
            }
            const string interesting = "{}[]()\"'`/\\*:;,.?+-=<>\n x0A";
            for (var n = 0; n < 2000; n++)
            {
                var chars = seed.ToCharArray();
                for (var k = random.Next(1, 4); k > 0; k--)
                    chars[random.Next(chars.Length)] = interesting[random.Next(interesting.Length)];
                ParsesOrFailsCleanly(new string(chars));
            }
        }
    }

    private static void ParsesOrFailsCleanly(string source)
    {
        try
        {
            QmlParser.Parse(source, "fuzz.qml");
        }
        catch (QmlSyntaxException e)
        {
            // A location inside the text: a reader that ran past the end would point past its last line.
            var lines = source.Split('\n');
            Assert.True(e.Location.Line >= 1 && e.Location.Line <= lines.Length
                && e.Location.Column >= 1 && e.Location.Column <= lines[e.Location.Line - 1].Length + 1,
                $"{e.Message} points outside this source:\n{source}");
        }
        catch (Exception e)
        {
            Assert.Fail($"{e.GetType().Name}: {e.Message}\n{source}");
        }
    }
}
