namespace Winshell.Tests;

/// <summary>
/// A pre-start failure is a dialog only when nobody can read it. The dialog itself is replaced, so these
/// tests never open one.
/// </summary>
public class StartupErrorTests
{
    [Theory]
    [InlineData(0, (int)StreamTarget.Other, true)]
    [InlineData(0, (int)StreamTarget.File, false)]
    [InlineData(0, (int)StreamTarget.Pipe, false)]
    [InlineData(1, (int)StreamTarget.Other, false)]
    public void A_dialog_is_only_for_an_error_nobody_else_can_read(int window, int stderr, bool dialog)
    {
        Assert.Equal(dialog, StartupError.NeedsDialog(window, (StreamTarget)stderr));
    }

    [Fact]
    public void No_console_asks_the_dialog_with_the_message_and_the_folder()
    {
        var seen = Asked(() => StartupError.Report("No .csproj at C:\\missing.", "C:\\configs", 0, StreamTarget.Other));

        var call = Assert.Single(seen);
        Assert.Equal("No .csproj at C:\\missing.", call.Message);
        Assert.Equal("C:\\configs", call.Folder);
    }

    [Fact]
    public void A_console_does_not_ask_the_dialog()
    {
        var seen = Asked(() => StartupError.Report("No .csproj at C:\\missing.", "C:\\configs", 1, StreamTarget.Other));

        Assert.Empty(seen);
    }

    [Fact]
    public void A_redirected_error_does_not_ask_the_dialog()
    {
        var seen = Asked(() =>
            StartupError.Report("No config named \"work\" in C:\\configs.", "C:\\configs", 0, StreamTarget.Pipe));

        Assert.Empty(seen);
    }

    [Fact]
    public void A_path_opens_that_folder_and_a_missing_root_opens_nothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "winshell-startup-error-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "MyShell.exe");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(root, StartupError.FolderFor(path, Path.Combine(root, "absent")));
            Assert.Equal(root, StartupError.FolderFor(null, root));
            Assert.Null(StartupError.FolderFor(null, Path.Combine(root, "absent")));
            Assert.Null(StartupError.FolderFor(Path.Combine(root, "gone", "MyShell.exe"), root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static List<(string Message, string? Folder)> Asked(Action report)
    {
        var seen = new List<(string Message, string? Folder)>();
        var previous = StartupError.Show;
        StartupError.Show = (message, folder) => seen.Add((message, folder));
        try
        {
            report();
            return seen;
        }
        finally
        {
            StartupError.Show = previous;
        }
    }
}
