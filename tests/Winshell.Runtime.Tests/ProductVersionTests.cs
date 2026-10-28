using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winshell.Runtime.Tests;

public class ProductVersionTests
{
    [Theory]
    [InlineData("0.3.0", 0, 3, 0)]
    [InlineData("0.3.0+abcdef", 0, 3, 0)]
    [InlineData("0.3.0-dev", 0, 3, 0)]
    [InlineData("0.0.0-dev+sha", 0, 0, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1", 1, 0, 0)]
    [InlineData("10.20.30-beta.1+build.7", 10, 20, 30)]
    [InlineData("", 0, 0, 0)]
    [InlineData("dev", 0, 0, 0)]
    [InlineData(null, 0, 0, 0)]
    public void Parse_reads_the_release_numbers(string? text, int major, int minor, int patch)
    {
        var version = ProductVersion.Parse(text);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Fact]
    public void Satisfies_is_at_least_the_requested_release()
    {
        var version = ProductVersion.Parse("0.3.0+abc");
        Assert.True(version.Satisfies(0, 3, null));
        Assert.True(version.Satisfies(0, 3, []));
        Assert.True(version.Satisfies(0, 2, ["later"]));
        Assert.True(version.Satisfies(0, 0, null));
        Assert.False(version.Satisfies(0, 3, ["not-released"]));
        Assert.False(version.Satisfies(0, 3, ["one", "two"]));
        Assert.False(version.Satisfies(0, 4, null));
        Assert.False(version.Satisfies(1, 0, []));
        Assert.True(version.Satisfies(-1, 9, ["x"]));
    }

    [Fact]
    public void The_assembly_version_is_the_release_please_manifest()
    {
        var manifest = ManifestVersion();
        var informational = typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Matches($"^{Regex.Escape(manifest)}(\\+\\S+)?$", informational);
        Assert.Equal(informational, ProductVersion.Current.Text);
        var parsed = ProductVersion.Parse(manifest);
        Assert.Equal(parsed.Major, ProductVersion.Current.Major);
        Assert.Equal(parsed.Minor, ProductVersion.Current.Minor);
        Assert.Equal(parsed.Patch, ProductVersion.Current.Patch);
        Assert.Equal((parsed.Major, parsed.Minor), Preprocessor.Version);
    }

    [Fact]
    public void Preprocessor_picks_the_branch_for_this_release()
    {
        var version = ProductVersion.Current;
        var (olderMajor, olderMinor) = Older(version);
        var text = Preprocessor.Run($"a\n//@ if hasVersion({olderMajor}, {olderMinor})\nb\n//@ else\nc\n//@ endif\n//@ if hasVersion({version.Major + 1}, 0)\nd\n//@ endif");
        Assert.Equal(
            ["a", $"//@ if hasVersion({olderMajor}, {olderMinor})", "b", "//@ else", "", "//@ endif", $"//@ if hasVersion({version.Major + 1}, 0)", "", "//@ endif"],
            text.Split('\n'));
    }

    [Fact]
    public void Preprocessor_keeps_a_feature_only_on_a_newer_release()
    {
        var version = ProductVersion.Current;
        var (olderMajor, olderMinor) = Older(version);
        var current = Preprocessor.Run($"//@ if hasVersion({version.Major}, {version.Minor}, [\"not-released\"])\nkept\n//@ else\ndropped\n//@ endif");
        Assert.Equal(
            [$"//@ if hasVersion({version.Major}, {version.Minor}, [\"not-released\"])", "", "//@ else", "dropped", "//@ endif"],
            current.Split('\n'));

        var past = Preprocessor.Run($"//@ if hasVersion({olderMajor}, {olderMinor}, [\"not-released\"])\nkept\n//@ endif");
        Assert.Equal(
            [$"//@ if hasVersion({olderMajor}, {olderMinor}, [\"not-released\"])", "kept", "//@ endif"],
            past.Split('\n'));
    }

    [Fact]
    public void Preprocessor_combines_version_checks_and_keeps_the_qt_language_level()
    {
        var version = ProductVersion.Current;
        var (olderMajor, olderMinor) = Older(version);
        var text = Preprocessor.Run($"//@ if hasVersion({olderMajor}, {olderMinor}) && !hasVersion({version.Major + 1}, 0)\nkept\n//@ endif\n//@ if hasQtVersion(6, 10)\nqt\n//@ endif\n//@ if hasQtVersion(7, 0)\nlater\n//@ endif");
        Assert.Equal(
        [
            $"//@ if hasVersion({olderMajor}, {olderMinor}) && !hasVersion({version.Major + 1}, 0)",
            "kept",
            "//@ endif",
            "//@ if hasQtVersion(6, 10)",
            "qt",
            "//@ endif",
            "//@ if hasQtVersion(7, 0)",
            "",
            "//@ endif",
        ], text.Split('\n'));
    }

    [Fact]
    public void Shell_hasVersion_matches_the_stamped_release()
    {
        var version = ProductVersion.Current;
        var (olderMajor, olderMinor) = Older(version);
        var root = TestShell.Run($$"""
            import Winshell
            import QtQml
            QtObject {
              property bool current: Shell.hasVersion({{version.Major}}, {{version.Minor}})
              property bool older: Shell.hasVersion({{olderMajor}}, {{olderMinor}}, ["not-released"])
              property bool feature: Shell.hasVersion({{version.Major}}, {{version.Minor}}, ["not-released"])
              property bool future: Shell.hasVersion({{version.Major + 1}}, 0)
              property bool qt: Shell.hasQtVersion(6, 10)
              property bool qtFuture: Shell.hasQtVersion(7, 0)
            }
            """, out var shell);
        using var _ = shell;
        Assert.True((bool)root.ReadProperty("current")!);
        Assert.True((bool)root.ReadProperty("older")!);
        Assert.False((bool)root.ReadProperty("feature")!);
        Assert.False((bool)root.ReadProperty("future")!);
        Assert.True((bool)root.ReadProperty("qt")!);
        Assert.False((bool)root.ReadProperty("qtFuture")!);
        Assert.Empty(shell.Messages);
    }

    private static (int Major, int Minor) Older(ProductVersion version) =>
        version.Minor > 0 ? (version.Major, version.Minor - 1) : (version.Major - 1, 0);

    private static string ManifestVersion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, ".release-please-manifest.json");
            if (File.Exists(path))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                return json.RootElement.GetProperty(".").GetString() ?? throw new InvalidOperationException("The release-please manifest has no version.");
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("The release-please manifest is not above the test output.");
    }
}
