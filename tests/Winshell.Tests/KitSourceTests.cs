using System.Text.RegularExpressions;
using FluentAssertions;

namespace Winshell.Tests;

/// <summary>
/// The Kit's colors are its tokens, its corners are the Look's, and the shell's surfaces draw with the Kit and spell out no size
/// or color of their own (ADR 0045). Winoma keeps the same rule for its own sources.
/// </summary>
[TestClass]
public sealed partial class KitSourceTests
{
    [TestMethod]
    public void Every_brush_the_kit_paints_with_is_a_token()
    {
        var brushes = Brush().Matches(Source("src/Winshell.Kit/Paint.cs")).Select(match => match.Groups["name"].Value).ToList();

        brushes.Should().Contain(["Background", "Accent", "Track"]);
        brushes.Where(brush => !Enum.TryParse<KitToken>(brush, out _))
            .Should().BeEmpty("a brush in the Kit's Paint is a KitToken, which a palette fills");
        Enum.GetNames<KitToken>().Where(token => !brushes.Contains(token) && !Enum.TryParse<PanelTone>(token, out _))
            .Should().BeEmpty("a KitToken is a brush in the Kit's Paint, or a tone");
    }

    [TestMethod]
    public void The_kit_spells_out_no_color_of_its_own()
    {
        var found = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Winshell.Kit"), "*.cs").Order(StringComparer.Ordinal))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) && SpelledColor().IsMatch(lines[i]))
                {
                    found.Add(Path.GetFileName(path) + ":" + (i + 1) + " " + lines[i].Trim());
                }
            }
        }

        found.Should().BeEmpty("the Kit's colors are its tokens, and the palette in effect fills them");
    }

    [TestMethod]
    public void The_kit_corner_token_reads_the_look_and_nothing_else()
    {
        var corners = Source("src/Winshell.Kit/Corners.cs");

        corners.Should().Contain("Look.Current.ControlCorner").And.Contain("Look.Current.CornerSize");
        corners.Should().NotContain("DwmSetWindowAttribute");
    }

    [TestMethod]
    [DataRow("the Bar", "src/Winshell.Kit/BarButton.cs")]
    [DataRow("a Panel", "src/Winshell/Surfaces/PanelWindow.cs")]
    [DataRow("a Toast", "src/Winshell.Kit/NotificationCards.cs")]
    public void Each_surface_draws_its_corner_from_the_kit_token(string surface, string file)
    {
        Source(file).Should().MatchRegex(@"CornerRadius = Corners\.(Control|Overlay)\b", surface + " draws its corner from the Kit's token");
    }

    [TestMethod]
    public void No_surface_spells_out_a_corner_of_its_own_for_a_card_or_a_panel()
    {
        var root = RepoRoot();
        var own = new List<string>();
        foreach (var path in Sources(Path.Combine(root, "src", "Winshell")))
        {
            own.AddRange(File.ReadAllLines(path)
                .Select((line, index) => (line, index))
                .Where(hit => hit.line.Contains("CornerRadius =", StringComparison.Ordinal) && !hit.line.Contains("Corners.", StringComparison.Ordinal))
                .Select(hit => Relative(root, path) + ":" + (hit.index + 1)));
        }

        own.Should().BeEmpty("a surface draws its corner from Corners");
    }

    [TestMethod]
    public void Only_the_kit_spells_out_sizes_and_colors()
    {
        var root = RepoRoot();
        var found = new List<string>();
        foreach (var path in Sources(Path.Combine(root, "src", "Winshell", "Surfaces")))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Breaks(lines[i]))
                {
                    found.Add(Relative(root, path) + ":" + (i + 1) + " " + lines[i].Trim());
                }
            }
        }

        found.Should().BeEmpty("sizes, resource lookups, and colors come from src/Winshell.Kit");
    }

    /// <summary>True when <paramref name="line"/> sets a size from a number, looks up a resource, or makes a color.</summary>
    private static bool Breaks(string line)
    {
        if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (line.Contains("Application.Current.Resources[", StringComparison.Ordinal)
            || line.Contains("Color.FromArgb", StringComparison.Ordinal)
            || line.Contains("new SolidColorBrush", StringComparison.Ordinal))
        {
            return true;
        }

        return Sized().Matches(line).Any(match => Numbers().Matches(match.Groups["args"].Value).Any(number => !IsZero(number.Value)));
    }

    private static bool IsZero(string number) => double.TryParse(number, System.Globalization.CultureInfo.InvariantCulture, out var value) && value == 0;

    private static IEnumerable<string> Sources(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Source(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Winshell.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("Winshell.slnx was not found above " + AppContext.BaseDirectory);
    }

    // "public static Brush Name =>": a brush the Kit hands out, not a method that makes one.
    [GeneratedRegex(@"^\s*public static Brush (?<name>[A-Z]\w*)\s*=>", RegexOptions.Multiline)]
    private static partial Regex Brush();

    // A color made from numbers, a hex color, or one of WinUI's named colors.
    [GeneratedRegex(@"Color\.FromArgb\([^)]*(?<![\w.])\d|0x[0-9A-Fa-f]{6,8}\b|\bColors\.[A-Z]")]
    private static partial Regex SpelledColor();

    // A Thickness or CornerRadius built from arguments, or a font size or spacing set to an expression.
    [GeneratedRegex(@"new (?:Thickness|CornerRadius)\((?<args>[^)]*)\)|\b(?:FontSize|Spacing|ColumnSpacing|RowSpacing)\s*=\s*(?<args>[^,;}]*)")]
    private static partial Regex Sized();

    // A numeric literal that stands on its own, not a digit inside a name such as Corners.Overlay2.
    [GeneratedRegex(@"(?<![\w.])-?\d+(?:\.\d+)?")]
    private static partial Regex Numbers();
}
