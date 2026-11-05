using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>The real <c>winshell inspect</c> against the probe's panel, over the control pipe.</summary>
[Trait("Category", "Slow")]
public sealed class InspectCommandTests : IDisposable, IClassFixture<ProbeBuild>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly Process _shell;
    private readonly string _pid;

    private (string, string)[] Folders =>
    [
        ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")),
        ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local")),
    ];

    public InspectCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "config"));
        Directory.CreateDirectory(Path.Combine(_dir, "local"));
        File.WriteAllText(Path.Combine(_dir, "config", ProbeShell.WindowMarker), "");
        _shell = ProbeShell.Start(Path.Combine(_dir, "config"), Path.Combine(_dir, "local"));
        _pid = _shell.Id.ToString(CultureInfo.InvariantCulture);
        Wait.Until(() => Run("tree").Output.Contains("ProbePage", StringComparison.Ordinal), "the panel in the tree");
    }

    public void Dispose()
    {
        if (!_shell.HasExited)
        {
            _shell.Kill();
            _shell.WaitForExit();
        }
        _shell.Dispose();
        Wait.Until(Removed, "the test folder to be free");
    }

    private bool Removed()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private ChildProcess Run(params string[] args) =>
        WinshellCli.Run(["inspect", "--pid", _pid, .. args], Folders);

    private static int Count(string tree, string type) =>
        JsonDocument.Parse(tree).RootElement.EnumerateArray().Count(window => window.GetProperty("content").GetProperty("type").GetString() == type);

    [Fact]
    public void The_tree_is_json_and_names_the_panel_and_its_button()
    {
        var result = Run("tree");

        Assert.Equal("", result.Error);
        result.AssertExitCode(0);
        Assert.Equal(1, Count(result.Output, "ProbePage"));
        Assert.Contains("\"name\": \"Open\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_opens_the_popup_and_the_tree_shows_it()
    {
        Assert.Equal(0, Count(Run("tree").Output, "ProbePopup"));

        // The window is named as well as the element, as a shell with a bar on each display must.
        var result = Run("click", "Open", "ProbePage");

        Assert.Equal("", result.Error);
        result.AssertExitCode(0);
        Assert.Equal("Clicked Open", result.Output.Trim());
        Wait.Until(() => Count(Run("tree").Output, "ProbePopup") == 1, "the popup to open");
    }

    [Fact]
    public void A_screenshot_writes_a_png_of_the_window()
    {
        var file = Path.Combine(_dir, "panel.png");

        var result = Run("screenshot", file, "ProbePage");

        Assert.Equal("", result.Error);
        result.AssertExitCode(0);
        Assert.Contains(file, result.Output, StringComparison.Ordinal);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], File.ReadAllBytes(file)[..4]);
    }

    [Fact]
    public void An_element_that_is_not_there_exits_1_and_says_so()
    {
        var result = Run("click", "NoSuchThing");

        result.AssertExitCode(1);
        Assert.Contains("No element is named NoSuchThing", result.Error, StringComparison.Ordinal);
    }
}
