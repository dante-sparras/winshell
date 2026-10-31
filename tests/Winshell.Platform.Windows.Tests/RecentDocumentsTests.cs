using Winshell.Platform.Windows.Services.Apps;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Recent documents are the shortcut names in a folder the test creates. Nothing here reads this PC's profile.
/// </summary>
public sealed class RecentDocumentsTests
{
    [Fact]
    public void Matching_uses_shortcut_names_in_the_given_folder()
    {
        var root = Temp();
        try
        {
            var folder = Path.Combine(root, "QueryToken");
            Directory.CreateDirectory(folder);
            var agenda = Path.Combine(folder, "Agenda Notes.lnk");
            Touch(agenda, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
            Touch(Path.Combine(folder, "Agenda.txt"), new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));
            var nested = Path.Combine(folder, "nested");
            Directory.CreateDirectory(nested);
            Touch(Path.Combine(nested, "Secret Agenda.lnk"), new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));

            var hits = RecentDocuments.Matching(folder, "AGENDA notes");
            var hit = Assert.Single(hits);
            Assert.Equal("Agenda Notes", hit["name"]);
            Assert.Equal(agenda, hit["path"]);
            Assert.StartsWith(folder, (string)hit["path"]!, StringComparison.OrdinalIgnoreCase);

            Assert.Empty(RecentDocuments.Matching(folder, "QueryToken"));
            Assert.Empty(RecentDocuments.Matching(folder, "secret"));
            Assert.Empty(RecentDocuments.Matching(folder, "agenda missing"));
            Assert.Empty(RecentDocuments.Matching(folder, ""));
            Assert.Empty(RecentDocuments.Matching(folder, "   "));
            Assert.Empty(RecentDocuments.Matching(Path.Combine(root, "missing"), "agenda"));
            Assert.Empty(RecentDocuments.Matching(null, "agenda"));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Matching_returns_the_newest_names_first_and_stops_at_twenty()
    {
        var folder = Temp();
        try
        {
            var same = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            Touch(Path.Combine(folder, "Beta.lnk"), same);
            Touch(Path.Combine(folder, "Able.lnk"), same);
            Touch(Path.Combine(folder, "Later.lnk"), same.AddMinutes(1));
            Assert.Equal(["Later", "Able", "Beta"], Names(RecentDocuments.Matching(folder, "a")));

            for (var i = 0; i < 21; i++)
                Touch(Path.Combine(folder, $"Doc {i:00}.lnk"), same.AddMinutes(i));
            var capped = RecentDocuments.Matching(folder, "doc");
            Assert.Equal(20, capped.Count);
            Assert.Equal("Doc 20", capped[0]["name"]);
            Assert.Equal("Doc 01", capped[^1]["name"]);
            Assert.DoesNotContain(capped, row => row["name"] as string == "Doc 00");
        }
        finally
        {
            Delete(folder);
        }
    }

    [Fact]
    public void A_blank_query_matches_nothing()
    {
        var folder = Temp();
        try
        {
            Touch(Path.Combine(folder, "Agenda.lnk"), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            var docs = new RecentDocuments(folder);
            var hit = Assert.Single(docs.Search("agenda"));
            Assert.Equal("Agenda", hit["name"]);
            Assert.Empty(docs.Search(""));
            Assert.Empty(docs.Search("   "));

            // The parameterless service is what the shell constructs. A blank query must not open a folder.
            var real = new RecentDocuments();
            Assert.Empty(real.Search(""));
            Assert.Empty(real.Search("   "));
            Assert.Empty(real.Search("\t"));
        }
        finally
        {
            Delete(folder);
        }
    }

    private static List<string> Names(IReadOnlyList<Dictionary<string, object?>> hits) =>
        hits.Select(hit => Assert.IsType<string>(hit["name"])).ToList();

    private static void Touch(string path, DateTime when)
    {
        File.WriteAllBytes(path, []);
        File.SetLastWriteTimeUtc(path, when);
    }

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "winshell-recent-documents", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
    }
}
