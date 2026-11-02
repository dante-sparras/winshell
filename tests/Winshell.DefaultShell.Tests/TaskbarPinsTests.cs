using System.Text.Json;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskbarPinsTests : IDisposable
{
    private static readonly string[] Defaults = ["Microsoft.Windows.Explorer", "MSEdge", "Microsoft.WindowsStore_8wekyb3d8bbwe!App"];

    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-taskbar-pins-").FullName;

    private string Json => Path.Combine(_folder, "data", "taskbar.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private TaskbarPins Open(ManualEventLoop loop, Log? log = null) => new(Json, loop, log);

    private void Seed(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Json)!);
        File.WriteAllText(Json, json);
    }

    [Fact]
    public void A_missing_file_gives_the_defaults()
    {
        using var pins = Open(new ManualEventLoop());

        Assert.Equal(Defaults, pins.Ids);
        Assert.False(File.Exists(Json));
    }

    [Fact]
    public void A_file_is_read_when_the_pins_are_opened()
    {
        Seed("""{ "pinned": ["Saved.App"] }""");

        using var pins = Open(new ManualEventLoop());

        Assert.Equal(["Saved.App"], pins.Ids);
    }

    [Fact]
    public void An_empty_pinned_list_is_kept_and_not_replaced_with_the_defaults()
    {
        Seed("""{"pinned":[]}""");

        using var pins = Open(new ManualEventLoop());

        Assert.Empty(pins.Ids);
    }

    [Fact]
    public void Pin_appends_an_id_and_ignores_a_duplicate_or_an_empty_one()
    {
        var loop = new ManualEventLoop();
        using var pins = Open(loop);

        pins.Pin("");
        pins.Pin("MSEdge");
        Assert.Equal(Defaults, pins.Ids);
        Assert.False(File.Exists(Json));

        pins.Pin("Contoso.Notes");
        pins.Pin("Contoso.Notes");
        pins.Pin("");

        Assert.Equal([.. Defaults, "Contoso.Notes"], pins.Ids);
        Assert.Equal(
            """{"pinned":["Microsoft.Windows.Explorer","MSEdge","Microsoft.WindowsStore_8wekyb3d8bbwe!App","Contoso.Notes"]}""",
            File.ReadAllText(Json));
        Assert.Equal(["taskbar.json"], Directory.GetFiles(Path.GetDirectoryName(Json)!).Select(Path.GetFileName));
    }

    [Fact]
    public void Unpin_removes_an_id_and_does_not_write_when_it_is_not_pinned()
    {
        var loop = new ManualEventLoop();
        using var pins = Open(loop);

        pins.Unpin("Not.Pinned");
        Assert.Equal(Defaults, pins.Ids);
        Assert.False(File.Exists(Json));

        pins.Unpin("MSEdge");
        var text = File.ReadAllText(Json);
        Assert.Equal(["Microsoft.Windows.Explorer", "Microsoft.WindowsStore_8wekyb3d8bbwe!App"], pins.Ids);
        Assert.Contains("Microsoft.Windows.Explorer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MSEdge", text, StringComparison.Ordinal);

        pins.Unpin("Not.Pinned");
        Assert.Equal(text, File.ReadAllText(Json));
        Assert.Equal(["Microsoft.Windows.Explorer", "Microsoft.WindowsStore_8wekyb3d8bbwe!App"], pins.Ids);
    }

    [Fact]
    public void Move_puts_a_pin_after_a_later_one_and_before_an_earlier_one()
    {
        var loop = new ManualEventLoop();
        using var pins = Open(loop);

        pins.Move("Microsoft.Windows.Explorer", "Microsoft.WindowsStore_8wekyb3d8bbwe!App");
        Assert.Equal(["MSEdge", "Microsoft.WindowsStore_8wekyb3d8bbwe!App", "Microsoft.Windows.Explorer"], pins.Ids);

        pins.Move("Microsoft.Windows.Explorer", "MSEdge");
        Assert.Equal(Defaults, pins.Ids);

        var text = File.ReadAllText(Json);
        pins.Move("MSEdge", "MSEdge");
        pins.Move("", "MSEdge");
        pins.Move("Missing", "MSEdge");
        pins.Move("MSEdge", "Missing");
        Assert.Equal(Defaults, pins.Ids);
        Assert.Equal(text, File.ReadAllText(Json));

        using var document = JsonDocument.Parse(text);
        var pinned = document.RootElement.GetProperty("pinned").EnumerateArray().Select(entry => entry.GetString()).ToArray();
        Assert.Equal(Defaults, pinned);
        Assert.Single(document.RootElement.EnumerateObject());
    }

    [Fact]
    public void A_hand_edit_is_followed_on_the_loop()
    {
        var loop = new ManualEventLoop();
        using var pins = Open(loop);
        pins.Pin("Contoso.Notes");
        loop.Process();

        File.WriteAllText(Json, """{"pinned":["By.Hand"]}""");

        Wait.Until(() => pins.Ids.SequenceEqual(["By.Hand"]), "the hand edit", loop.Process);
    }

    [Fact]
    public void A_hand_edit_that_removes_every_pin_gives_an_empty_list()
    {
        var loop = new ManualEventLoop();
        using var pins = Open(loop);
        pins.Pin("Contoso.Notes");
        loop.Process();

        File.WriteAllText(Json, """{"pinned":[]}""");

        Wait.Until(() => pins.Ids.Count == 0, "the empty list", loop.Process);
        Assert.Empty(pins.Ids);
    }

    [Fact]
    public void A_write_the_watcher_reads_back_does_not_raise_PropertyChanged()
    {
        var loop = new ManualEventLoop();
        using var pins = Open(loop);
        var raised = new List<string>();
        pins.PropertyChanged += (_, change) => raised.Add(change.PropertyName!);

        pins.Pin("Contoso.Notes");
        Assert.Equal([nameof(TaskbarPins.Ids)], raised);

        Wait.ForNothingToHappen(TimeSpan.FromMilliseconds(200));
        loop.Process();
        Assert.Equal([nameof(TaskbarPins.Ids)], raised);

        File.WriteAllText(Json, """{"pinned":["By.Hand"]}""");
        Wait.Until(() => pins.Ids.SequenceEqual(["By.Hand"]), "the hand edit", loop.Process);
        Assert.Equal([nameof(TaskbarPins.Ids), nameof(TaskbarPins.Ids)], raised);
    }

    [Fact]
    public void Broken_JSON_keeps_the_current_ids()
    {
        Seed("{");
        var lines = new List<string>();
        var loop = new ManualEventLoop();
        using var pins = Open(loop, new Log { Sink = (_, _, message) => lines.Add(message) });

        Assert.Equal(Defaults, pins.Ids);
        Assert.Contains(lines, line => line.Contains("Could not read", StringComparison.Ordinal));

        lines.Clear();
        pins.Pin("Contoso.Notes");
        loop.Process();
        File.WriteAllText(Json, "{");

        Wait.Until(() => lines.Exists(line => line.Contains("Could not read", StringComparison.Ordinal)), "the bad file", loop.Process);
        Assert.Equal([.. Defaults, "Contoso.Notes"], pins.Ids);
    }

    [Fact]
    public void A_file_with_no_pinned_array_keeps_the_current_ids()
    {
        Seed("""{"other":true}""");

        using var pins = Open(new ManualEventLoop());

        Assert.Equal(Defaults, pins.Ids);
    }

    [Fact]
    public void A_hand_edit_with_no_pinned_array_keeps_the_current_ids()
    {
        var loop = new ManualEventLoop();
        using var pins = Open(loop);
        pins.Pin("Contoso.Notes");
        loop.Process();
        var raised = 0;
        pins.PropertyChanged += (_, _) => raised++;

        File.WriteAllText(Json, """{"other":true}""");
        Wait.ForNothingToHappen(TimeSpan.FromMilliseconds(200));
        loop.Process();

        File.WriteAllText(Json, """{"pinned":["By.Hand"]}""");
        Wait.Until(() => pins.Ids.SequenceEqual(["By.Hand"]), "the hand edit", loop.Process);
        Assert.Equal(1, raised);
    }
}
