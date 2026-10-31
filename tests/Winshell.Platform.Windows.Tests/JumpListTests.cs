using Winshell.Platform.Windows.Services.Apps;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>Jump list reads. The Windows reader is not called, so nothing here touches this PC's profile.</summary>
public class JumpListTests
{
    [Fact]
    public void Names_keep_recent_first_and_drop_duplicates_blanks_and_the_eleventh()
    {
        var recent = Enumerable.Range(0, 12).Select(i => i == 2 ? "   " : $"Recent {i}").ToArray();
        var read = JumpListRead.From(recent, ["recent 1", "Frequent"]);

        Assert.True(read.Available);
        // Ten recent names, the blank and the eleventh dropped, then a frequent name that is not already there.
        Assert.Equal(
            ["Recent 0", "Recent 1", "Recent 3", "Recent 4", "Recent 5", "Recent 6", "Recent 7", "Recent 8", "Recent 9", "Recent 10", "Frequent"],
            read.Names);
    }

    [Fact]
    public void Both_categories_missing_cannot_be_read_and_one_empty_category_can()
    {
        Assert.False(JumpListRead.From(null, null).Available);
        var empty = JumpListRead.From([], null);
        Assert.True(empty.Available);
        Assert.Empty(empty.Names);
        Assert.Equal(["Notes"], JumpListRead.From(null, ["Notes"]).Names);
    }

    [Fact]
    public void ForApp_asks_the_reader_and_a_blank_id_does_not()
    {
        var reader = new ScriptReader();
        reader.Result["Contoso.Notes"] = JumpListRead.From(["Budget.xlsx"], ["Budget.xlsx", "Notes.txt"]);
        using var engine = new QmlEngine(StandardModules.Create(), new ManualEventLoop());
        var lists = new JumpListService(engine, reader);

        var found = lists.ForApp("Contoso.Notes");
        Assert.True(found.Available);
        Assert.Equal(["Budget.xlsx", "Notes.txt"], found.Destinations);
        Assert.Equal(["Contoso.Notes"], reader.Asked);

        var blank = lists.ForApp("  ");
        Assert.False(blank.Available);
        Assert.Empty(blank.Destinations);
        Assert.Equal(["Contoso.Notes"], reader.Asked);

        reader.Result["Contoso.Notes"] = JumpListRead.Missing;
        var missing = lists.ForApp("Contoso.Notes");
        Assert.Same(found, missing);
        Assert.False(missing.Available);
        Assert.Empty(missing.Destinations);
    }

    private sealed class ScriptReader : IJumpListReader
    {
        public List<string> Asked { get; } = [];

        public Dictionary<string, JumpListRead> Result { get; } = new(StringComparer.OrdinalIgnoreCase);

        public JumpListRead Read(string appUserModelId)
        {
            Asked.Add(appUserModelId);
            return Result.TryGetValue(appUserModelId, out var read) ? read : JumpListRead.Missing;
        }
    }
}
