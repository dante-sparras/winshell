using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// Runs each configuration in <c>Conformance/</c> as a shell and compares its log with the <c>// expect:</c>
/// lines in its root file. See <c>Conformance/README.md</c>.
/// </summary>
public class ConformanceTests
{
    private static readonly string Corpus = Path.Combine(AppContext.BaseDirectory, "Conformance");

    public static TheoryData<string> Cases()
    {
        var cases = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Corpus, "*.qml").Order(StringComparer.Ordinal))
            cases.Add(Path.GetFileName(file));
        foreach (var dir in Directory.EnumerateDirectories(Corpus).Order(StringComparer.Ordinal))
        {
            if (File.Exists(Path.Combine(dir, "shell.qml")))
                cases.Add(Path.GetFileName(dir) + "/");
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case_logs_what_it_expects(string name)
    {
        var root = name.EndsWith('/') ? Path.Combine(Corpus, name.TrimEnd('/'), "shell.qml") : Path.Combine(Corpus, name);
        var dir = Path.GetDirectoryName(root)! + Path.DirectorySeparatorChar;
        var expected = File.ReadLines(root)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("// expect:", StringComparison.Ordinal))
            .Select(l => l["// expect:".Length..].Trim())
            .ToList();

        var actual = new List<string>();
        var log = new Log
        {
            Sink = (level, category, message) =>
            {
                message = message.Replace(dir, "", StringComparison.OrdinalIgnoreCase);
                actual.Add(level == LogLevel.Info && category.Length == 0
                    ? message
                    : $"{level.ToString().ToUpperInvariant()} {category}: {message}");
            },
        };
        var loop = new ManualEventLoop { Log = log };
        using (var host = new ShellHost(root, loop, null, log) { WatchFiles = false })
        {
            host.Start();
            loop.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(string.Join('\n', expected), string.Join('\n', actual));
    }
}
