using Winshell.Docs;

namespace Winshell.ShellTests;

/// <summary>
/// Each reference page is generated from the public API of <c>Winshell.Shell</c>, <c>Winshell.Services</c> and
/// <c>Winshell.Core</c>. Each <c>csharp</c> sample in the docs and the README is text from <see cref="GuideSamples"/>,
/// which this project builds.
/// </summary>
public sealed class DocsTests
{
    private static readonly string Repo = TestSupport.Repo.Root;

    [Fact]
    public void Every_csharp_example_is_compiled()
    {
        var source = File.ReadAllText(Path.Combine(Repo, "tests", "Winshell.Shell.Tests", "GuideSamples.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var examples = Examples().ToList();
        Assert.NotEmpty(examples);
        var problems = new List<string>();
        foreach (var (file, line, code) in examples)
            if (code.Length == 0)
                problems.Add($"{Path.GetRelativePath(Repo, file)}:{line}: empty csharp fence");
            else if (!source.Contains(code, StringComparison.Ordinal))
                problems.Add($"{Path.GetRelativePath(Repo, file)}:{line}: csharp fence is not in GuideSamples.cs");
        Assert.True(problems.Count == 0, "C# examples that are not compiled:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// Each <c>docs/reference/winshell*.md</c> page is what <c>scripts/generate-reference.cs</c> writes from the
    /// public API and its /// comments: this project compiles that file, so the pages can't drift from the code.
    /// </summary>
    [Fact]
    public void The_reference_is_what_the_generator_writes()
    {
        var folder = Path.Combine(Repo, "docs", "reference");
        var stale = ReferencePages.Stale(folder, ReferencePages.Generate(folder));
        Assert.True(stale.Count == 0,
            $"Out of date in docs/reference: {string.Join(", ", stale)}. Run: dotnet run scripts/generate-reference.cs");
    }

    /// <summary>The <c>```csharp</c> blocks of every page, with the line each one starts on.</summary>
    private static IEnumerable<(string File, int Line, string Code)> Examples()
    {
        foreach (var file in Pages())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "```csharp")
                    continue;
                var start = i + 1;
                var end = start;
                while (end < lines.Length && lines[end].Trim() != "```")
                    end++;
                yield return (file, start + 1, string.Join('\n', lines[start..end]));
                i = end;
            }
        }
    }

    /// <summary>The pages in <c>docs/</c>, outside <c>docs/agents/</c>, and the README.</summary>
    private static IEnumerable<string> Pages()
    {
        var agents = Path.Combine(Repo, "docs", "agents") + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(Path.Combine(Repo, "docs"), "*.md", SearchOption.AllDirectories)
            .Where(file => !file.StartsWith(agents, StringComparison.OrdinalIgnoreCase))
            .Append(Path.Combine(Repo, "README.md"))
            .Order(StringComparer.Ordinal);
    }
}
