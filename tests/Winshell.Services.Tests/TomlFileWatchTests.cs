namespace Winshell.Services.Tests;

/// <summary>A <see cref="TomlFile"/> follows a file changed by something else, on its loop.</summary>
public sealed class TomlFileWatchTests
{
    private sealed class Settings(string path, IEventLoop loop, Log log) : TomlFile(path, loop, log)
    {
        public string Name { get; set => Set(ref field, value); } = "default";

        public bool On { get; set => Set(ref field, value); } = true;
    }

    private static Settings Open(TomlFixture fixture) => new(fixture.FilePath, fixture.Loop, fixture.Log);

    [Fact]
    public void A_hand_edit_is_followed_on_the_loop()
    {
        using var fixture = new TomlFixture("name = \"file\"\n");
        using var settings = Open(fixture);
        var thread = Environment.CurrentManagedThreadId;
        var threads = new List<int>();
        settings.PropertyChanged += (_, _) => threads.Add(Environment.CurrentManagedThreadId);

        File.WriteAllText(fixture.FilePath, "name = \"by hand\"\n");

        Wait.Until(() => settings.Name == "by hand", "the hand edit", fixture.Loop.Process);
        Assert.Equal([thread], threads);
        Assert.Empty(fixture.Messages);
    }

    [Fact]
    public void A_hand_edit_after_a_write_becomes_the_properties()
    {
        using var fixture = new TomlFixture("name = \"file\"\n");
        using var settings = Open(fixture);

        settings.On = false;
        fixture.Loop.Process();
        File.WriteAllText(fixture.FilePath, "name = \"by hand\"\n");

        Wait.Until(() => settings.Name == "by hand", "the hand edit after a write", fixture.Loop.Process);
    }

    [Fact]
    public void A_watched_write_is_still_in_the_file_after_the_watch_runs()
    {
        using var fixture = new TomlFixture("name = \"mine\"\n");
        using var settings = Open(fixture);
        var changes = new List<string>();
        settings.Name = "next";
        settings.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        fixture.Loop.Process();

        // The watch event is delivered on another thread. Let it run, then read on the loop.
        Wait.ForNothingToHappen(TimeSpan.FromMilliseconds(200));
        fixture.Loop.Process();

        Assert.Contains("name = \"next\"", fixture.Text, StringComparison.Ordinal);
        Assert.Equal("next", settings.Name);
        Assert.DoesNotContain(nameof(Settings.Name), changes);
    }

    [Fact]
    public void A_file_fixed_by_hand_clears_the_error_and_a_good_read_replaces_what_could_not_be_written()
    {
        using var fixture = new TomlFixture("name = \"broken");
        using var settings = Open(fixture);
        settings.Name = "assigned";
        fixture.Loop.Process();
        Assert.NotEqual("", settings.Error);

        File.WriteAllText(fixture.FilePath, "name = \"ok\"\n");

        Wait.Until(() => settings.Name == "ok", "the fixed file", fixture.Loop.Process);
        Assert.Equal("", settings.Error);
        Assert.True(settings.Loaded);
    }

    [Fact]
    public void A_hand_save_that_lands_before_the_replace_is_kept_and_the_assigned_value_goes_into_it()
    {
        using var fixture = new TomlFixture("name = \"file\"\n");
        using var settings = Open(fixture);
        settings.BeforeReplace = () =>
        {
            settings.BeforeReplace = null;
            File.WriteAllText(fixture.FilePath, "name = \"by hand\"\n");
        };

        settings.On = false;
        fixture.Loop.Process();

        Assert.Equal("by hand", settings.Name);
        Assert.Contains("name = \"by hand\"", fixture.Text, StringComparison.Ordinal);
        Assert.Contains("on = false", fixture.Text, StringComparison.Ordinal);
        Assert.Equal([fixture.FilePath], Directory.GetFiles(fixture.Directory));
    }

    [Fact]
    public void A_hand_save_before_the_watch_is_armed_becomes_the_properties()
    {
        using var fixture = new TomlFixture();
        var path = Path.Combine(fixture.Directory, "later", "settings.toml");
        using var settings = new Settings(path, fixture.Loop, fixture.Log);
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));

        settings.Name = "chosen";
        fixture.Loop.Process();
        // The folder and the file exist. The watch moves to them on a later turn, so this save is too early for it.
        File.WriteAllText(path, "name = \"by hand\"\n");

        Wait.Until(() => settings.Name == "by hand", "the save before the watch moved", fixture.Loop.Process);
        Assert.Contains("name = \"by hand\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("later")]
    [InlineData("later/nested/config")]
    public void A_hand_created_file_in_a_new_folder_is_followed(string relativeFolder)
    {
        using var fixture = new TomlFixture();
        var directory = Path.Combine(fixture.Directory, relativeFolder);
        using var settings = new Settings(Path.Combine(directory, "settings.toml"), fixture.Loop, fixture.Log);
        Assert.False(settings.Loaded);

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.toml"), "name = \"by hand\"");

        Wait.Until(() => settings.Name == "by hand", "the file in the new folder", fixture.Loop.Process);
        Assert.True(settings.Loaded);
        Assert.Empty(fixture.Messages);
    }

    [Fact]
    public void A_file_in_a_folder_deleted_and_made_again_is_followed()
    {
        using var fixture = new TomlFixture();
        var directory = Path.Combine(fixture.Directory, "config");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.toml");
        File.WriteAllText(path, "name = \"first\"");
        using var settings = new Settings(path, fixture.Loop, fixture.Log);
        Assert.Equal("first", settings.Name);

        Directory.Delete(directory, true);
        Wait.Until(() => !settings.Loaded, "the deleted folder", fixture.Loop.Process);

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "name = \"second\"");

        Wait.Until(() => settings.Name == "second", "the file in the new folder", fixture.Loop.Process);
        Assert.Empty(fixture.Messages);
    }

    [Fact]
    public void A_deleted_file_keeps_the_values_and_a_later_write_makes_it_again()
    {
        using var fixture = new TomlFixture("name = \"first\"");
        using var settings = Open(fixture);
        Assert.True(settings.Loaded);

        File.Delete(fixture.FilePath);

        Wait.Until(() => !settings.Loaded, "the deleted file", fixture.Loop.Process);
        Assert.Equal("first", settings.Name);
        Assert.Empty(fixture.Messages);

        settings.On = false;
        fixture.Loop.Process();
        Assert.Contains("name = \"first\"", fixture.Text, StringComparison.Ordinal);
        Assert.Contains("on = false", fixture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_not_a_deleted_file()
    {
        // Declared first, so it is disposed last. A watch can post until the file is disposed.
        using var woken = new AutoResetEvent(false);
        using var fixture = new TomlFixture("name = \"first\"");
        using var settings = Open(fixture);
        fixture.Loop.Woken += () => woken.Set();

        File.WriteAllText(fixture.FilePath, "name = \"second\"");
        Assert.True(woken.WaitOne(TimeSpan.FromSeconds(30)), "The write was not noticed.");
        using (new FileStream(fixture.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            fixture.Loop.Process();

        Assert.True(settings.Loaded);
        Assert.Equal("first", settings.Name);
    }

    [Fact]
    public void A_disposed_file_does_not_follow_or_write()
    {
        using var fixture = new TomlFixture("name = \"first\"");
        var settings = Open(fixture);
        settings.Dispose();

        File.WriteAllText(fixture.FilePath, "name = \"second\"");
        Wait.ForNothingToHappen(TimeSpan.FromMilliseconds(100));
        fixture.Loop.Process();
        Assert.Equal("first", settings.Name);

        settings.Name = "third";
        fixture.Loop.Process();
        Assert.Equal("name = \"second\"", fixture.Text);
    }
}
