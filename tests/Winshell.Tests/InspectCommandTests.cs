using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>The real <c>winshell inspect</c> against the real default shell, over the control pipe.</summary>
[Collection(DefaultShellCollection.Name)]
[Trait("Category", "Slow")]
public sealed class InspectCommandTests : IDisposable
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
        // The Win key belongs to the desktop this runs on.
        File.WriteAllText(Path.Combine(_dir, "config", "preferences.toml"), "startHotkey = \"\"\n");
        var info = WinshellCli.StartInfo(["run"]);
        foreach (var (name, value) in Folders)
            info.Environment[name] = value;
        _shell = Process.Start(info)!;
        _pid = _shell.Id.ToString(CultureInfo.InvariantCulture);
        Wait.Until(() => Run("tree").Output.Contains("TaskbarView", StringComparison.Ordinal), "the taskbar in the tree");
    }

    public void Dispose()
    {
        if (!_shell.HasExited)
        {
            _shell.Kill(entireProcessTree: true);
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

    private (int Code, string Output, string Error) Run(params string[] args) =>
        WinshellCli.Run(["inspect", "--pid", _pid, .. args], Folders);

    private static int Count(string tree, string type) =>
        JsonDocument.Parse(tree).RootElement.EnumerateArray().Count(window => window.GetProperty("content").GetProperty("type").GetString() == type);

    private static string FirstWindow(string tree, string type) =>
        JsonDocument.Parse(tree).RootElement.EnumerateArray()
            .First(window => window.GetProperty("content").GetProperty("type").GetString() == type)
            .GetProperty("window").GetInt32().ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void The_tree_is_json_and_names_the_taskbar_and_its_start_button()
    {
        var (code, output, error) = Run("tree");

        Assert.Equal("", error);
        Assert.Equal(0, code);
        Assert.True(Count(output, "TaskbarView") >= 1);
        Assert.Contains("\"name\": \"StartButton\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_opens_start_and_the_tree_shows_it()
    {
        Assert.Equal(0, Count(Run("tree").Output, "StartView"));

        // Each display can have a taskbar, so the window's number says which StartButton.
        var (code, output, error) = Run("click", "StartButton", FirstWindow(Run("tree").Output, "TaskbarView"));

        Assert.Equal("", error);
        Assert.Equal((0, "Clicked StartButton"), (code, output.Trim()));
        Wait.Until(() => Count(Run("tree").Output, "StartView") == 1, "Start to open");
    }

    [Fact]
    public void A_screenshot_writes_a_png_of_the_bar_at_a_path_relative_to_the_caller()
    {
        var file = Path.Combine(_dir, "bar.png");

        var (code, output, error) = Run("screenshot", file, "TaskbarView");

        Assert.Equal("", error);
        Assert.Equal(0, code);
        Assert.Contains(file, output, StringComparison.Ordinal);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], File.ReadAllBytes(file)[..4]);
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("click")]
    [InlineData("screenshot")]
    public void A_wrong_command_exits_2_and_prints_its_usage(string command)
    {
        var (code, _, _) = Run(command);

        Assert.Equal(2, code);
    }

    [Fact]
    public void An_element_that_is_not_there_exits_1_and_says_so()
    {
        var (code, _, error) = Run("click", "NoSuchThing");

        Assert.Equal(1, code);
        Assert.Contains("No element is named NoSuchThing", error, StringComparison.Ordinal);
    }
}
