namespace Winshell.Qml.Tests;

/// <summary>
/// Every prefix and every one-character deletion of the shipped QML either parses
/// or throws <see cref="QmlSyntaxException"/> at a position in that input.
/// A different exception would crash a shell instead of reporting a config error.
/// </summary>
public class QmlParserCorpusTests
{
    [Fact]
    [Trait("Category", "Corpus")]
    public void Prefixes_and_deletions_of_shipped_qml_parse_or_locate_the_error()
    {
        var files = Corpus().ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Parallel.For(0, text.Length + 1, length => Check(file, text[..length]));
            Parallel.For(0, text.Length, index => Check(file, string.Concat(text.AsSpan(0, index), text.AsSpan(index + 1))));
        }
    }

    private static void Check(string file, string text)
    {
        try
        {
            QmlParser.Parse(text, file);
        }
        catch (QmlSyntaxException error)
        {
            if (!Inside(text, error.Location))
                throw new InvalidOperationException(
                    $"{Path.GetFileName(file)} reported {error.Location} outside an input of {text.Length} characters: {error.Reason}");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(file)} ({text.Length} characters) threw {error.GetType().Name}: {error.Message}", error);
        }
    }

    /// <summary>Line and column are 1-based. The position just past the last character is the end of the file.</summary>
    private static bool Inside(string text, SourceLocation location)
    {
        if (location.Line < 1 || location.Column < 1)
            return false;
        var line = 1;
        var column = 1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (line == location.Line && column == location.Column)
                return true;
            if (i == text.Length)
                break;
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return false;
    }

    private static IEnumerable<string> Corpus()
    {
        var repo = FindRepo();
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(repo, "tests", "Winshell.Runtime.Tests", "Conformance"), "*.qml"))
            yield return file;
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(repo, "src", "Winshell", "DefaultShell"), "*.qml"))
            yield return file;
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "winshell.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("winshell.slnx was not found above the test directory.");
    }
}
