#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Text;
using Tomlyn.Parsing;

namespace Winshell.Services.Tests;

/// <summary><see cref="TomlFile"/> reads a file into typed properties and writes an assigned property back into it.</summary>
public sealed class TomlFileTests
{
    private sealed class Settings(string path, IEventLoop loop, Log log, Func<string, object, string>? check = null)
        : TomlFile(path, loop, log, check)
    {
        [TomlComment("Who this is.")]
        public string Name { get; set => Set(ref field, value); } = "default";

        public bool On { get; set => Set(ref field, value); } = true;

        public int Size { get; set => Set(ref field, value); } = 3;

        public double Scale { get; set => Set(ref field, value); } = 1.5;

        [TomlComment("The tiles, in order.\nOne per key.")]
        public IReadOnlyList<string> Tiles { get; set => Set(ref field, value); } = ["wifi"];

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Hidden { get; set => Set(ref field, value); } = [];

        // Not a key: it has no public setter.
        public string Shown => Name.ToUpperInvariant();
    }

    /// <summary>launcher.toml: null until the file has pins, and [] is a choice.</summary>
    private sealed class Pins(string path, IEventLoop loop, Log log) : TomlFile(path, loop, log)
    {
        [TomlComment("The apps pinned to Start, in order.")]
        public IReadOnlyList<string>? Apps { get; set => Set(ref field, value); }
    }

    private sealed class Big(string path, IEventLoop loop, Log log) : TomlFile(path, loop, log)
    {
        public long Number { get; set => Set(ref field, value); }

        public string Name { get; set => Set(ref field, value); } = "default";
    }

    private sealed class Loose(string path, IEventLoop loop, Log log) : TomlFile(path, loop, log)
    {
        public object? Gone { get; set => Set(ref field, value); }

        public int? Maybe { get; set => Set(ref field, value); }

        public IReadOnlyDictionary<string, object?>? Taskbar { get; set => Set(ref field, value); }
    }

    private static Settings Open(TomlFixture fixture, Func<string, object, string>? check = null) =>
        new(fixture.FilePath, fixture.Loop, fixture.Log, check);

    private static string BackdropCheck(string key, object value) =>
        key == "name" && value is not ("acrylic" or "mica") ? "is not \"acrylic\" or \"mica\"" : "";

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] items) =>
        items.ToDictionary(item => item.Key, item => item.Value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [Fact]
    public void Values_are_read_from_the_file_and_a_key_it_does_not_have_keeps_its_default()
    {
        using var fixture = new TomlFixture("""
            name = 'from "file"'
            size = 7
            scale = 2
            hidden = [{ id = "Contoso.Chat", name = "Chat" }, { id = "Contoso.Mail" }]
            unknown = 1
            """);
        using var settings = Open(fixture);

        Assert.True(settings.Loaded);
        Assert.Equal("from \"file\"", settings.Name);
        Assert.Equal(7, settings.Size);
        Assert.Equal(2.0, settings.Scale);
        Assert.True(settings.On);
        Assert.Equal(["Contoso.Chat", "Contoso.Mail"], settings.Hidden.Select(row => row["id"]));
        Assert.Equal("Chat", settings.Hidden[0]["name"]);
        Assert.False(settings.Hidden[1].ContainsKey("name"));
        Assert.Empty(fixture.Messages);
    }

    [Fact]
    public void An_array_of_tables_reads_as_rows()
    {
        using var fixture = new TomlFixture("""
            [[hidden]]
            id = "Contoso.Chat"
            name = "Chat"

            [[hidden]]
            id = "Contoso.Mail"
            """);
        using var settings = Open(fixture);

        Assert.Equal(["Contoso.Chat", "Contoso.Mail"], settings.Hidden.Select(row => row["id"]));
    }

    [Fact]
    public void A_changed_array_of_tables_keeps_its_comment_and_unknown_key()
    {
        using var fixture = new TomlFixture("""
            [[hidden]]
            id = "Contoso.Chat" # chat
            name = "Chat"
            extra = 1

            [[hidden]]
            id = "Contoso.Mail"

            [other]
            kept = true

            """);
        using var settings = Open(fixture);

        settings.Hidden = [Row(("id", "Contoso.Chat"), ("name", "Renamed"))];
        fixture.Loop.Process();

        var text = fixture.Text;
        Assert.False(SyntaxParser.Parse(text).HasErrors);
        Assert.Single(text.Split("[[hidden]]").Skip(1));
        Assert.Contains("id = \"Contoso.Chat\" # chat", text, StringComparison.Ordinal);
        Assert.Contains("name = \"Renamed\"", text, StringComparison.Ordinal);
        Assert.Contains("extra = 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden =", text, StringComparison.Ordinal);
        Assert.Contains("[other]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_is_null_until_the_file_has_it_and_an_empty_one_is_a_choice()
    {
        using var fixture = new TomlFixture();
        using (var none = new Pins(fixture.FilePath, fixture.Loop, fixture.Log))
        {
            Assert.Null(none.Apps);
            Assert.False(none.Loaded);
            none.Save();
            Assert.DoesNotContain("apps", fixture.Text, StringComparison.Ordinal);
        }

        using var first = new Pins(fixture.FilePath, fixture.Loop, fixture.Log);
        first.Apps = [];
        fixture.Loop.Process();
        Assert.Equal("# The apps pinned to Start, in order.\napps = []\n", fixture.Text.TrimStart('\r', '\n'));

        using var second = new Pins(fixture.FilePath, fixture.Loop, fixture.Log);
        Assert.NotNull(second.Apps);
        Assert.Empty(second.Apps);
    }

    [Fact]
    public void A_new_file_has_every_value_with_its_comment_above_it()
    {
        using var fixture = new TomlFixture();
        using var settings = Open(fixture);

        settings.Save();

        Assert.Equal("""
            # Who this is.
            name = "default"

            on = true

            size = 3

            scale = 1.5

            # The tiles, in order.
            # One per key.
            tiles = ["wifi"]

            hidden = []

            """, fixture.Text);
        Assert.Empty(fixture.Messages);
    }

    [Fact]
    public void Save_makes_the_folder_when_there_is_none()
    {
        using var fixture = new TomlFixture();
        var path = Path.Combine(fixture.Directory, "later", "nested", "settings.toml");
        using var settings = new Settings(path, fixture.Loop, fixture.Log);

        settings.Save();

        Assert.Contains("name = \"default\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.True(settings.Loaded);
    }

    [Fact]
    public void Writing_changes_only_the_values_and_keeps_what_the_user_wrote()
    {
        using var fixture = new TomlFixture("""
            # My settings.
            name = "mine" # set by hand

            # Not Winshell's.
            unknown = 1
            size = 3

            [other]
            kept = true

            """);
        using var settings = Open(fixture);

        settings.Name = "changed \\ \"quoted\"";
        settings.Hidden = [Row(("id", "Contoso.Chat"), ("name", "Chat"))];
        fixture.Loop.Process();

        Assert.Equal("""
            # My settings.
            name = "changed \\ \"quoted\"" # set by hand

            # Not Winshell's.
            unknown = 1
            size = 3

            on = true

            scale = 1.5

            # The tiles, in order.
            # One per key.
            tiles = ["wifi"]

            hidden = [{ id = "Contoso.Chat", name = "Chat" }]

            [other]
            kept = true

            """, fixture.Text);
        Assert.Empty(fixture.Messages);
    }

    [Fact]
    public void Assignments_in_one_turn_of_the_loop_are_one_write_on_the_loop()
    {
        using var fixture = new TomlFixture("name = \"mine\"\n");
        using var settings = Open(fixture);
        var writes = 0;
        settings.BeforeReplace = () => writes++;
        var changes = new List<string>();
        settings.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        settings.Name = "one";
        settings.On = false;
        settings.Size = 4;

        Assert.Equal([nameof(Settings.Name), nameof(Settings.On), nameof(Settings.Size)], changes);
        Assert.Contains("name = \"mine\"", fixture.Text, StringComparison.Ordinal);
        fixture.Loop.Process();
        Assert.Equal(1, writes);
        Assert.Contains("name = \"one\"", fixture.Text, StringComparison.Ordinal);
        Assert.Contains("on = false", fixture.Text, StringComparison.Ordinal);
        Assert.Contains("size = 4", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_leaves_no_temporary_file()
    {
        using var fixture = new TomlFixture("name = \"mine\"\n");
        using var settings = Open(fixture);

        settings.Name = "next";
        fixture.Loop.Process();

        Assert.Equal([fixture.FilePath], Directory.GetFiles(fixture.Directory));
    }

    [Fact]
    public void A_null_value_takes_its_key_out_and_the_comments_around_it_stay()
    {
        using var fixture = new TomlFixture("""
            # Above gone.
            gone = 1
            # Above maybe.
            maybe = 2

            """);
        using var loose = new Loose(fixture.FilePath, fixture.Loop, fixture.Log);
        Assert.Equal(1L, loose.Gone);
        Assert.Equal(2, loose.Maybe);

        loose.Gone = null;
        loose.Maybe = null;
        fixture.Loop.Process();

        Assert.Contains("# Above gone.\n", fixture.Text, StringComparison.Ordinal);
        Assert.Contains("# Above maybe.\n", fixture.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("=", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_key_a_null_property_never_held_stays_in_the_file()
    {
        using var fixture = new TomlFixture("maybe = \"text\"\ngone = 1\n");
        using var loose = new Loose(fixture.FilePath, fixture.Loop, fixture.Log);
        Assert.Null(loose.Maybe);

        loose.Gone = 2L;
        fixture.Loop.Process();

        Assert.Contains("maybe = \"text\"", fixture.Text, StringComparison.Ordinal);
        Assert.Contains("gone = 2", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dotted_key_is_updated_in_place()
    {
        using var fixture = new TomlFixture("taskbar.centered = false # stay\n");
        using var loose = new Loose(fixture.FilePath, fixture.Loop, fixture.Log);
        Assert.Equal(false, loose.Taskbar!["centered"]);

        loose.Taskbar = Row(("centered", true));
        fixture.Loop.Process();

        Assert.Contains("taskbar.centered = true # stay", fixture.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("taskbar =", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void One_bad_value_does_not_stop_the_rest_of_the_load()
    {
        using var fixture = new TomlFixture("size = \"x\"\nname = \"kept\"\n");
        using var settings = Open(fixture);
        Assert.Equal("kept", settings.Name);
        Assert.Equal(3, settings.Size);
        Assert.Contains(fixture.Messages, m => m.Contains("size is the wrong type, so it was left unchanged", StringComparison.Ordinal));

        settings.Name = "next";
        fixture.Loop.Process();
        Assert.Contains("size = \"x\"", fixture.Text, StringComparison.Ordinal);

        settings.Size = 8;
        fixture.Loop.Process();
        Assert.Contains("size = 8", fixture.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("size = \"x\"", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_value_is_reported_once_and_not_at_each_write()
    {
        using var fixture = new TomlFixture("size = \"x\"\n");
        using var settings = Open(fixture);
        settings.Name = "one";
        fixture.Loop.Process();
        settings.Name = "two";
        fixture.Loop.Process();

        Assert.Single(fixture.Messages);
    }

    [Theory]
    [InlineData("\"false\"")]
    [InlineData("[false]")]
    [InlineData("{ on = false }")]
    public void A_bool_property_leaves_a_string_array_or_table_unchanged(string value)
    {
        using var fixture = new TomlFixture($"on = {value}\nname = \"mine\"\n");
        using var settings = Open(fixture);
        Assert.True(settings.On);
        Assert.Contains(fixture.Messages, m => m.Contains("on is the wrong type", StringComparison.Ordinal));

        settings.Name = "next";
        fixture.Loop.Process();
        Assert.Contains($"on = {value}", fixture.Text, StringComparison.Ordinal);

        settings.On = false;
        fixture.Loop.Process();
        Assert.Contains("on = false", fixture.Text, StringComparison.Ordinal);
        Assert.DoesNotContain($"on = {value}", fixture.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("0.0", false)]
    [InlineData("0.5", true)]
    [InlineData("nan", false)]
    public void A_bool_property_takes_a_number_and_leaves_its_spelling(string value, bool expected)
    {
        using var fixture = new TomlFixture($"on = {value}\n");
        using var settings = Open(fixture);
        Assert.Equal(expected, settings.On);

        settings.Name = "next";
        fixture.Loop.Process();

        Assert.Contains($"on = {value}\n", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_property_keeps_its_value_when_the_file_has_another_type()
    {
        using var fixture = new TomlFixture("name = 0x0078D4\nsize = 4\n");
        using var settings = Open(fixture);

        Assert.Equal("default", settings.Name);
        Assert.Equal(4, settings.Size);

        settings.Size = 5;
        fixture.Loop.Process();
        Assert.Contains("name = 0x0078D4\n", fixture.Text, StringComparison.Ordinal);
        Assert.Contains("size = 5\n", fixture.Text, StringComparison.Ordinal);

        settings.Name = "#0078d4";
        fixture.Loop.Process();
        Assert.Contains("name = \"#0078d4\"\n", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_with_an_item_of_another_type_is_left_whole()
    {
        using var fixture = new TomlFixture("tiles = [\"wifi\", 2]\n");
        using var settings = Open(fixture);

        Assert.Equal(["wifi"], settings.Tiles);
        Assert.Contains(fixture.Messages, m => m.Contains("tiles is the wrong type", StringComparison.Ordinal));
    }

    [Fact]
    public void An_integer_past_an_int_is_left_and_one_past_2_to_the_53_stays_exact()
    {
        using var fixture = new TomlFixture("size = 3000000000\n");
        using (var settings = Open(fixture))
            Assert.Equal(3, settings.Size);

        File.WriteAllText(fixture.FilePath, "number = 9223372036854775807\nname = \"mine\"\n");
        using var big = new Big(fixture.FilePath, fixture.Loop, fixture.Log);
        Assert.Equal(long.MaxValue, big.Number);

        big.Name = "next";
        fixture.Loop.Process();
        Assert.Contains("number = 9223372036854775807", fixture.Text, StringComparison.Ordinal);

        big.Number = long.MaxValue - 1;
        fixture.Loop.Process();
        Assert.Contains("number = 9223372036854775806", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_value_keeps_the_one_in_use_and_names_the_key()
    {
        using var fixture = new TomlFixture("name = \"Mica\"\nsize = 4\n");
        using var settings = Open(fixture, BackdropCheck);

        Assert.Equal("default", settings.Name);
        Assert.Equal(4, settings.Size);
        Assert.Contains(fixture.Messages, m => m.Contains("name is not \"acrylic\" or \"mica\", so it was left unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_the_check_takes_is_set_without_a_word()
    {
        using var fixture = new TomlFixture("name = \"mica\"\n");
        using var settings = Open(fixture, BackdropCheck);

        Assert.Equal("mica", settings.Name);
        Assert.Empty(fixture.Messages);
    }

    [Fact]
    public void Another_change_does_not_rewrite_a_refused_value()
    {
        using var fixture = new TomlFixture("name = \"Mica\"\nsize = 4\n");
        using var settings = Open(fixture, BackdropCheck);

        settings.Size = 5;
        fixture.Loop.Process();

        Assert.Contains("name = \"Mica\"\n", fixture.Text, StringComparison.Ordinal);
        Assert.Contains("size = 5\n", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_does_not_parse_names_the_line_blocks_the_write_and_keeps_the_values()
    {
        using var fixture = new TomlFixture("name = \"broken");
        using var settings = Open(fixture);
        Assert.Equal("default", settings.Name);
        Assert.False(settings.Loaded);
        Assert.Contains("settings.toml[1:", settings.Error, StringComparison.Ordinal);
        Assert.Contains("]:", settings.Error, StringComparison.Ordinal);
        Assert.Contains(fixture.Messages, m => m.Contains("TomlFile", StringComparison.Ordinal) && m.Contains("settings.toml[1:", StringComparison.Ordinal));

        var before = File.ReadAllBytes(fixture.FilePath);
        settings.On = false;
        fixture.Loop.Process();
        settings.Save();

        Assert.Equal(before, File.ReadAllBytes(fixture.FilePath));
        Assert.Contains(fixture.Messages, m => m.Contains("left unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void A_torn_read_is_written_from_the_copy_that_parses()
    {
        using var fixture = new TomlFixture("name = \"broken");
        using var settings = Open(fixture);
        File.WriteAllText(fixture.FilePath, "name = \"disk\" # keep\n\n[other]\nkept = true\n");

        settings.On = false;
        fixture.Loop.Process();

        var text = fixture.Text;
        Assert.False(SyntaxParser.Parse(text).HasErrors);
        Assert.Contains("name = \"disk\" # keep", text, StringComparison.Ordinal);
        Assert.DoesNotContain("name = \"default\"", text, StringComparison.Ordinal);
        Assert.Equal("disk", settings.Name);
        Assert.Equal("", settings.Error);
        Assert.Contains("[other]", text, StringComparison.Ordinal);
        Assert.Contains("kept = true", text, StringComparison.Ordinal);
        Assert.Contains("on = false", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_keeps_a_hand_edit_it_had_not_seen_and_writes_only_what_was_assigned()
    {
        using var fixture = new TomlFixture("name = \"mine\"\nsize = 3\n");
        using var settings = Open(fixture);
        File.WriteAllText(fixture.FilePath, "name = \"by hand\"\nsize = 9\n");

        settings.Size = 4;
        fixture.Loop.Process();

        Assert.Contains("name = \"by hand\"", fixture.Text, StringComparison.Ordinal);
        Assert.Contains("size = 4", fixture.Text, StringComparison.Ordinal);
        Assert.Equal("by hand", settings.Name);
        Assert.Equal(4, settings.Size);
    }

    [Fact]
    public void A_read_that_fails_writes_nothing()
    {
        using var fixture = new TomlFixture("name = \"broken");
        using var settings = Open(fixture);
        const string Disk = "name = \"disk\" # keep\n";
        File.WriteAllText(fixture.FilePath, Disk);
        using (new FileStream(fixture.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings.On = false;
            fixture.Loop.Process();
            Assert.Contains(fixture.Messages, m => m.Contains("left unchanged", StringComparison.Ordinal));
            Assert.DoesNotContain(fixture.Messages, m => m.Contains("failed to write", StringComparison.Ordinal));
            Assert.NotEqual("", settings.Error);
        }
        Assert.Equal(Disk, fixture.Text);
        Assert.Equal("default", settings.Name);
    }

    [Fact]
    public void A_value_assigned_while_the_file_was_broken_waits_for_a_later_write()
    {
        using var fixture = new TomlFixture("name = \"broken");
        using var settings = Open(fixture);
        settings.On = false;
        fixture.Loop.Process();
        File.WriteAllText(fixture.FilePath, "name = \"fixed\"\n");

        settings.Save();

        Assert.Equal("fixed", settings.Name);
        Assert.Contains("on = false", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_order_mark_is_kept()
    {
        using var fixture = new TomlFixture();
        File.WriteAllBytes(fixture.FilePath, [0xEF, 0xBB, 0xBF, .. "name = \"mine\"\n"u8]);
        using var settings = Open(fixture);
        Assert.Equal("mine", settings.Name);

        settings.Name = "next";
        fixture.Loop.Process();

        var written = File.ReadAllBytes(fixture.FilePath);
        Assert.Equal([0xEF, 0xBB, 0xBF], written[..3]);
        Assert.Contains("name = \"next\"", Encoding.UTF8.GetString(written[3..]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_crlf_file_stays_crlf()
    {
        using var fixture = new TomlFixture("name = \"mine\" # hand\r\n");
        using var settings = Open(fixture);

        settings.Name = "yours";
        fixture.Loop.Process();

        var text = fixture.Text;
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("name = \"yours\" # hand\r\n", text, StringComparison.Ordinal);
        Assert.Contains("on = true\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_goes_through_a_hard_link_and_keeps_it()
    {
        using var fixture = new TomlFixture("name = \"mine\"\n");
        var other = Path.Combine(fixture.Directory, "other.toml");
        Assert.True(CreateHardLinkW(other, fixture.FilePath, IntPtr.Zero), "CreateHardLinkW failed");
        using var settings = Open(fixture);

        settings.Name = "next";
        fixture.Loop.Process();
        settings.Name = "again";
        fixture.Loop.Process();

        Assert.Contains("name = \"again\"", File.ReadAllText(other), StringComparison.Ordinal);
        Assert.Contains("name = \"again\"", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_goes_through_a_symbolic_link_and_keeps_it()
    {
        using var fixture = new TomlFixture();
        var real = Path.Combine(fixture.Directory, "real.toml");
        File.WriteAllText(real, "name = \"mine\"\n");
        try
        {
            File.CreateSymbolicLink(fixture.FilePath, real);
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) == 1314)
        {
            // ERROR_PRIVILEGE_NOT_HELD. This process cannot create a symbolic link. The hard-link test covers the in-place path.
            return;
        }
        using var settings = Open(fixture);

        settings.Name = "next";
        fixture.Loop.Process();

        Assert.Contains("name = \"next\"", File.ReadAllText(real), StringComparison.Ordinal);
        Assert.NotNull(new FileInfo(fixture.FilePath).LinkTarget);
    }

    [Fact]
    public void A_write_that_fails_is_reported_and_the_value_waits_for_the_next_one()
    {
        using var fixture = new TomlFixture("name = \"mine\"\n");
        using var settings = Open(fixture);
        var blocked = false;
        settings.BeforeReplace = () =>
        {
            if (blocked)
                throw new IOException("the disk is full");
        };
        blocked = true;
        settings.Name = "next";
        fixture.Loop.Process();
        Assert.Contains(fixture.Messages, m => m.Contains("failed to write", StringComparison.Ordinal) && m.Contains("the disk is full", StringComparison.Ordinal));
        Assert.Equal("name = \"mine\"\n", fixture.Text);
        Assert.Equal([fixture.FilePath], Directory.GetFiles(fixture.Directory));

        blocked = false;
        settings.Save();
        Assert.Contains("name = \"next\"", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_finishes_a_write_on_its_way()
    {
        using var fixture = new TomlFixture("name = \"mine\"\n");
        var settings = Open(fixture);

        settings.Name = "next";
        settings.Dispose();

        Assert.Contains("name = \"next\"", fixture.Text, StringComparison.Ordinal);
    }
}
