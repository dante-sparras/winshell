using System.Text.Json;
using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

/// <summary>The shell reads its own keys from the merged settings and leaves the rest unread.</summary>
[TestClass]
public sealed class ShellConfigTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winoma-tests", Guid.NewGuid().ToString("N"));

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root);

    [TestCleanup]
    public void Delete() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void The_shell_keys_are_read_and_a_missing_file_is_the_default()
    {
        var parsed = ShellConfig.Parse(
            """
            {
              "taskbar": "hidden",
              "barVisible": false,
              "barEdge": 2,
              "extensions": ["Winoma.Desktop.dll"],
              "bar": {
                "centerAnchor": "winoma.clock",
                "layout": { "left": [{ "id": "demo.sheet" }] }
              },
              "theme": "tokyo-night"
            }
            """);

        parsed.Notes.Should().BeEmpty();
        parsed.Taskbar.Should().Be(TaskbarSetting.Hidden);
        parsed.HidesTaskbar.Should().BeTrue();
        parsed.BarVisible.Should().BeFalse();
        parsed.BarEdge.Should().Be(BarEdge.Left);
        parsed.Extensions.Should().Equal("Winoma.Desktop.dll");
        parsed.Bar.Should().NotBeNull();
        parsed.Bar!.CenterAnchor.Should().Be("winoma.clock");
        parsed.Bar.Layout!.Left.Should().ContainSingle().Which.Id.Should().Be("demo.sheet");

        var missing = ShellConfig.Read(Path.Combine(_root, "settings.json"));
        missing.HidesTaskbar.Should().BeFalse();
        missing.BarVisible.Should().BeTrue();
        missing.BarEdge.Should().Be(BarEdge.Top);
        missing.Bar.Should().BeNull();
        missing.Extensions.Should().BeEmpty();
        missing.Notes.Should().Contain(note => note.Contains("missing", StringComparison.Ordinal));
    }

    [TestMethod]
    public void A_named_edge_is_read_and_a_bad_one_stays_on_top()
    {
        var named = ShellConfig.Parse("""{ "barEdge": "bottom", "barVisible": true, "taskbar": null, "extensions": null }""");
        named.BarEdge.Should().Be(BarEdge.Bottom);
        named.HidesTaskbar.Should().BeFalse();
        named.Extensions.Should().BeEmpty();
        named.Notes.Should().BeEmpty();

        var bad = ShellConfig.Parse("""{ "barEdge": 9, "barVisible": "yes", "taskbar": 1, "extensions": "dll", "bar": [] }""");
        bad.BarEdge.Should().Be(BarEdge.Top);
        bad.BarVisible.Should().BeTrue();
        bad.HidesTaskbar.Should().BeFalse();
        bad.Bar.Should().BeNull();
        bad.Extensions.Should().BeEmpty();
        bad.Notes.Should().Equal(
            "taskbar is not a word",
            "barVisible is not true or false",
            "barEdge is not an edge",
            "bar is not an object",
            "extensions is not a list");
    }

    [TestMethod]
    public void An_include_applies_first_and_the_file_itself_replaces_the_list()
    {
        File.WriteAllText(Path.Combine(_root, "shipped.json"), """{ "taskbar": "hidden", "extensions": ["Winoma.Desktop.dll"], "barEdge": 0 }""");
        File.WriteAllText(
            Path.Combine(_root, "settings.json"),
            """{ "include": ["shipped.json"], "extensions": ["other.dll"], "barVisible": false }""");

        var read = ShellConfig.Read(Path.Combine(_root, "settings.json"));

        read.Notes.Should().BeEmpty();
        read.HidesTaskbar.Should().BeTrue();
        read.BarVisible.Should().BeFalse();
        read.BarEdge.Should().Be(BarEdge.Top);
        read.Extensions.Should().Equal("other.dll");
    }

    [TestMethod]
    public void A_null_removes_the_extensions_an_include_set()
    {
        File.WriteAllText(Path.Combine(_root, "shipped.json"), """{ "extensions": ["Winoma.Desktop.dll"], "taskbar": "hidden" }""");
        File.WriteAllText(Path.Combine(_root, "settings.json"), """{ "include": ["shipped.json"], "extensions": null }""");

        var read = ShellConfig.Read(Path.Combine(_root, "settings.json"));

        read.Extensions.Should().BeEmpty();
        read.HidesTaskbar.Should().BeTrue();
        read.Notes.Should().BeEmpty();
    }

    [TestMethod]
    public void Text_that_is_not_json_throws()
    {
        var act = () => ShellConfig.Parse("nope");
        act.Should().Throw<JsonException>();
    }
}
