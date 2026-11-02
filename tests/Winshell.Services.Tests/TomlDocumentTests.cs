using Tomlyn.Parsing;

namespace Winshell.Services.Tests;

/// <summary><see cref="TomlDocument"/> reads a file's text as plain values and edits one key, leaving the rest of the text.</summary>
public sealed class TomlDocumentTests
{
    private static TomlDocument Parse(string text)
    {
        Assert.True(TomlDocument.TryParse("settings.toml", text, out var document, out var error), error);
        return document;
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] items) =>
        items.ToDictionary(item => item.Key, item => item.Value);

    private static List<object?> List(params object?[] items) => [.. items];

    private static void AssertParses(string text) => Assert.False(SyntaxParser.Parse(text).HasErrors, text);

    private static int Count(string text, string value) => text.Split(value).Length - 1;

    [Fact]
    public void Values_are_plain_types()
    {
        var values = Parse("""
            name = 'from "file"'
            on = true
            size = 7
            scale = 1.5
            tiles = ["wifi", 2]
            when = 1979-05-27
            hidden = [{ id = "Contoso.Chat", name = "Chat" }, { id = "Contoso.Mail" }]

            [other]
            kept = true

            [[rows]]
            id = "a"
            """).Values;

        Assert.Equal("from \"file\"", values["name"]);
        Assert.Equal(true, values["on"]);
        Assert.Equal(7L, values["size"]);
        Assert.Equal(1.5, values["scale"]);
        Assert.Equal(["wifi", 2L], Assert.IsAssignableFrom<IReadOnlyList<object?>>(values["tiles"]));
        Assert.Equal("1979-05-27", values["when"]);
        var hidden = Assert.IsAssignableFrom<IReadOnlyList<object?>>(values["hidden"]);
        Assert.Equal("Chat", Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(hidden[0])["name"]);
        Assert.False(Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(hidden[1]).ContainsKey("name"));
        Assert.Equal(true, Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(values["other"])["kept"]);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<object?>>(values["rows"]);
        Assert.Equal("a", Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(Assert.Single(rows))["id"]);
    }

    [Fact]
    public void A_key_is_added_under_its_comment_at_the_end_of_the_top_level_keys()
    {
        var document = Parse("""
            # My settings.
            name = "mine" # set by hand

            # Not Winshell's.
            unknown = 1
            size = 3

            [other]
            kept = true

            """);

        document.Set("name", "changed \\ \"quoted\"");
        document.Set("on", true);
        document.Set("tiles", new[] { "wifi" }, "The tiles, in order.\nOne per key.");
        document.Set("hidden", List(Row(("id", "Contoso.Chat"), ("name", "Chat"))));

        Assert.Equal("""
            # My settings.
            name = "changed \\ \"quoted\"" # set by hand

            # Not Winshell's.
            unknown = 1
            size = 3

            on = true

            # The tiles, in order.
            # One per key.
            tiles = ["wifi"]

            hidden = [{ id = "Contoso.Chat", name = "Chat" }]

            [other]
            kept = true

            """, document.ToString());
    }

    [Fact]
    public void A_key_added_to_a_file_without_a_last_new_line_starts_on_its_own_line()
    {
        var document = Parse("size = 3");

        document.Set("size", 4L);
        document.Set("name", "default", "Who this is.");

        Assert.Equal("size = 4\n\n# Who this is.\nname = \"default\"\n", document.ToString());
    }

    [Fact]
    public void Null_takes_the_key_out_and_the_comments_around_it_stay()
    {
        var document = Parse("""
            size = 3
            # Above gone.
            gone = 1
            # Above name.
            name = "kept"

            """);

        document.Set("gone", null);

        Assert.Equal("size = 3\n# Above gone.\n# Above name.\nname = \"kept\"\n", document.ToString());
        Assert.False(document.Values.ContainsKey("gone"));
    }

    [Fact]
    public void A_comment_above_the_first_key_stays_when_the_key_goes()
    {
        var document = Parse("# Above gone.\ngone = 1\nname = \"kept\"\n");

        document.Set("gone", null);

        Assert.Equal("# Above gone.\nname = \"kept\"\n", document.ToString());
    }

    [Fact]
    public void Comments_stay_when_every_key_goes()
    {
        var document = Parse("# Above one.\none = 1\n# Above two.\ntwo = 2\n");

        document.Set("one", null);
        document.Set("two", null);
        Assert.Equal("# Above one.\n# Above two.\n", document.ToString());

        document.Set("three", 3L);
        Assert.Equal("# Above one.\n# Above two.\nthree = 3\n", document.ToString());
    }

    [Fact]
    public void Comments_stay_above_the_first_table_when_every_top_level_key_goes()
    {
        var document = Parse("# Above gone.\ngone = 1\n\n[other]\nkept = true\n");

        document.Set("gone", null);

        Assert.StartsWith("# Above gone.\n", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("[other]\nkept = true\n", document.ToString(), StringComparison.Ordinal);
        AssertParses(document.ToString());
    }

    [Fact]
    public void A_comment_stays_when_a_table_loses_its_last_key()
    {
        var document = Parse("[other]\n# Above gone.\ngone = 1\n");

        document.Set("other", Row(("gone", null)));

        Assert.Equal("[other]\n# Above gone.\n", document.ToString());
    }

    [Fact]
    public void Indentation_does_not_stay_when_the_key_goes()
    {
        var document = Parse("  gone = 1\nname = \"kept\"\n");

        document.Set("gone", null);

        Assert.Equal("name = \"kept\"\n", document.ToString());
    }

    [Fact]
    public void A_comment_above_a_lone_table_stays_when_the_table_goes()
    {
        var document = Parse("# Above other.\n[other]\nkept = true\n");

        document.Set("other", null);

        Assert.Equal("# Above other.\n", document.ToString());
    }

    [Fact]
    public void Null_takes_out_a_table_and_a_key_the_file_does_not_have_stays_out()
    {
        var document = Parse("name = \"kept\"\n\n# Above other.\n[other]\nkept = true\n");

        document.Set("other", null);
        document.Set("never", null);

        Assert.Contains("# Above other.", document.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("[other]", document.ToString(), StringComparison.Ordinal);
        Assert.Equal(["name"], document.Values.Keys);
    }

    [Fact]
    public void Text_that_does_not_parse_is_refused_and_names_the_line()
    {
        Assert.False(TomlDocument.TryParse("settings.toml", "name = \"broken", out var document, out var error));
        Assert.Null(document);
        Assert.Contains("settings.toml[1:", error, StringComparison.Ordinal);
        Assert.Contains("]:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_array_of_tables_is_not_written_a_second_time()
    {
        var document = Parse("""
            # apps
            [[hidden]]
            id = "Contoso.Chat" # chat
            name = "Chat"

            [[hidden]]
            id = "Contoso.Mail"

            [other]
            kept = true

            """);

        document.Set("hidden", document.Values["hidden"]);
        document.Set("name", "renamed");

        var text = document.ToString();
        AssertParses(text);
        Assert.Equal(2, Count(text, "[[hidden]]"));
        Assert.DoesNotContain("hidden =", text, StringComparison.Ordinal);
        Assert.Contains("# chat", text, StringComparison.Ordinal);
        Assert.Contains("[other]", text, StringComparison.Ordinal);
        Assert.Contains("kept = true", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_table_row_keeps_its_comment_and_unknown_key()
    {
        var document = Parse("""
            [[hidden]]
            id = "Contoso.Chat" # chat
            name = "Chat"
            extra = 1

            [[hidden]]
            id = "Contoso.Mail"

            [other]
            kept = true

            """);

        document.Set("hidden", List(Row(("id", "Contoso.Chat"), ("name", "Renamed"))));

        var text = document.ToString();
        AssertParses(text);
        Assert.Equal(1, Count(text, "[[hidden]]"));
        Assert.Contains("id = \"Contoso.Chat\" # chat", text, StringComparison.Ordinal);
        Assert.Contains("name = \"Renamed\"", text, StringComparison.Ordinal);
        Assert.Contains("extra = 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden =", text, StringComparison.Ordinal);
        Assert.Contains("[other]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_row_goes_after_the_last_one()
    {
        var document = Parse("[[hidden]]\nid = \"a\"\n\n[other]\nkept = true\n");

        document.Set("hidden", List(Row(("id", "a")), Row(("id", "b"), ("name", "B"))));

        var text = document.ToString();
        AssertParses(text);
        Assert.True(text.IndexOf("id = \"b\"", StringComparison.Ordinal) < text.IndexOf("[other]", StringComparison.Ordinal));
        Assert.Equal(2, Count(text, "[[hidden]]"));
        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<object?>>(document.Values["hidden"]).Count);
    }

    [Fact]
    public void No_rows_are_written_as_an_empty_array_so_the_key_stays()
    {
        var document = Parse("[[hidden]]\nid = \"a\"\n\n[other]\nkept = true\n");

        document.Set("hidden", List());

        var text = document.ToString();
        AssertParses(text);
        Assert.DoesNotContain("[[hidden]]", text, StringComparison.Ordinal);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<object?>>(document.Values["hidden"]));
    }

    [Fact]
    public void An_unchanged_value_keeps_its_spelling_and_item_comment()
    {
        const string Original = """
            name = 'mine'
            hidden = [
              { id = "Contoso.Chat", name = "Chat" }, # chat
            ]
            scale = 1.0
            when = 1979-05-27
            hex = 0x10
            grouped = 1_000
            size = 3
            """;
        var document = Parse(Original);

        foreach (var (key, value) in document.Values)
            document.Set(key, value);
        document.Set("size", 4L);

        Assert.Equal(Original.Replace("size = 3", "size = 4", StringComparison.Ordinal), document.ToString());
    }

    [Fact]
    public void A_changed_array_item_keeps_its_comment()
    {
        var document = Parse("""
            hidden = [
              { id = "Contoso.Chat", name = "Chat" }, # chat
            ]
            size = 3

            """);

        document.Set("hidden", List(Row(("id", "Contoso.Chat"), ("name", "Renamed"))));

        var text = document.ToString();
        Assert.Contains("name = \"Renamed\"", text, StringComparison.Ordinal);
        Assert.Contains("# chat", text, StringComparison.Ordinal);
        Assert.Contains("id = \"Contoso.Chat\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_inside_a_changed_value_keeps_its_text()
    {
        var document = Parse("""
            row = { when = 1979-05-27 07:32:00, n = 1 }

            times = [07:32:00, 1979-05-27]

            [item]
            name = "Chat"
            when = 1979-05-27

            """);

        document.Set("item", Row(("name", "Mail"), ("when", "1979-05-27")));
        document.Set("row", Row(("when", "1979-05-27T07:32:00"), ("n", 2L)));
        document.Set("times", List("07:32:00", "1979-05-27", "later"));

        var text = document.ToString();
        Assert.Contains("name = \"Mail\"\nwhen = 1979-05-27", text, StringComparison.Ordinal);
        Assert.Contains("when = 1979-05-27 07:32:00", text, StringComparison.Ordinal);
        Assert.Contains("[07:32:00, 1979-05-27, \"later\"]", text, StringComparison.Ordinal);
        Assert.Contains("n = 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"1979-05-27\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"07:32:00\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"1979-05-27T07:32:00\"", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "row ="));
        Assert.Equal(1, Count(text, "times ="));
        Assert.Equal(1, Count(text, "[item]"));
    }

    [Fact]
    public void A_key_added_to_a_comment_only_crlf_file_uses_crlf()
    {
        var document = Parse("# header\r\n");

        document.Set("name", "default");

        var text = document.ToString();
        Assert.Contains("# header\r\n", text, StringComparison.Ordinal);
        Assert.Contains("name = \"default\"\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_added_to_a_crlf_file_uses_crlf()
    {
        var document = Parse("name = \"mine\" # hand\r\n");

        document.Set("name", "yours");
        document.Set("on", true);
        document.Set("hidden", List(Row(("id", "a"))), "The apps.\nOne per row.");

        var text = document.ToString();
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("name = \"yours\" # hand\r\n", text, StringComparison.Ordinal);
        Assert.Contains("on = true\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_leading_comment_stays_above_the_first_key_added()
    {
        var document = Parse("# header\n\n[other]\nkept = true\n");
        document.Set("name", "default");
        var text = document.ToString();
        Assert.True(text.IndexOf("# header", StringComparison.Ordinal) < text.IndexOf("name =", StringComparison.Ordinal));
        Assert.Contains("[other]", text, StringComparison.Ordinal);

        document = Parse("# header comment\n");
        document.Set("name", "default");
        Assert.StartsWith("# header comment\n", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_integer_past_2_to_the_53_stays_exact()
    {
        var document = Parse("big = 9223372036854775807\nname = \"mine\"\n");
        Assert.Equal(long.MaxValue, document.Values["big"]);

        document.Set("name", "next");
        Assert.Contains("big = 9223372036854775807", document.ToString(), StringComparison.Ordinal);

        document.Set("big", long.MinValue);
        Assert.Contains("big = -9223372036854775808", document.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("E+", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_quoted_key_is_the_same_key()
    {
        var document = Parse("\"name\" = \"quoted\" # q\n'size' = 9\n");
        Assert.Equal("quoted", document.Values["name"]);
        Assert.Equal(9L, document.Values["size"]);

        document.Set("name", "next");

        var text = document.ToString();
        Assert.Contains("\"name\" = \"next\" # q", text, StringComparison.Ordinal);
        Assert.Contains("'size' = 9", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "name"));
    }

    [Fact]
    public void A_dotted_key_is_the_value_and_is_updated_in_place()
    {
        var document = Parse("taskbar.centered = false # stay\nname = \"mine\"\n");
        Assert.Equal(false, Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(document.Values["taskbar"])["centered"]);

        document.Set("name", "next");
        Assert.Contains("taskbar.centered = false # stay", document.ToString(), StringComparison.Ordinal);

        document.Set("taskbar", Row(("centered", true), ("added", 1L)));

        var text = document.ToString();
        Assert.Contains("taskbar.centered = true # stay", text, StringComparison.Ordinal);
        Assert.Contains("taskbar.added = 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("taskbar =", text, StringComparison.Ordinal);
        AssertParses(text);
    }

    [Fact]
    public void A_table_is_updated_in_place_and_keeps_a_key_it_was_not_given()
    {
        var document = Parse("[taskbar]\ncentered = false # stay\nunknown = 1\nold = 2\n");

        document.Set("taskbar", Row(("centered", true), ("old", null), ("added", "x")));

        var text = document.ToString();
        Assert.Contains("centered = true # stay", text, StringComparison.Ordinal);
        Assert.Contains("unknown = 1", text, StringComparison.Ordinal);
        Assert.Contains("added = \"x\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "[taskbar]"));
    }

    [Fact]
    public void An_inline_table_is_updated_in_place()
    {
        var document = Parse("row = { a = 1, b = 2, c = 3 } # stay\n");

        document.Set("row", Row(("a", 9L), ("c", null), ("d", "new")));

        var text = document.ToString();
        AssertParses(text);
        Assert.Contains("# stay", text, StringComparison.Ordinal);
        Assert.Equal(Row(("a", 9L), ("b", 2L), ("d", "new")), Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(document.Values["row"]));
    }

    [Fact]
    public void A_value_of_another_type_replaces_the_old_one_and_its_comment_stays()
    {
        var document = Parse("on = \"false\" # stay\nsize = 3\n");

        document.Set("on", true);
        document.Set("size", "big");

        Assert.Equal("on = true # stay\nsize = \"big\"\n", document.ToString());
    }

    [Fact]
    public void A_whole_double_stays_a_float()
    {
        var document = Parse("scale = 1.5\n");

        document.Set("scale", 2.0);
        document.Set("added", 3.0);

        Assert.Equal(2.0, document.Values["scale"]);
        Assert.Equal(3.0, document.Values["added"]);
        Assert.Contains("scale = 2.0", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Values_of_other_clr_shapes_are_written_as_their_plain_type()
    {
        var document = Parse("");

        document.Set("size", 3);
        document.Set("scale", 1.5f);
        document.Set("sizes", new[] { 1, 2 });

        Assert.Equal(3L, document.Values["size"]);
        Assert.Equal(1.5, document.Values["scale"]);
        Assert.Equal([1L, 2L], Assert.IsAssignableFrom<IReadOnlyList<object?>>(document.Values["sizes"]));
    }
}
