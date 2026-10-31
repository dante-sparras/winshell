using System.Runtime.InteropServices;

namespace Winshell.Tests;

/// <summary>
/// A pre-start failure is a dialog only when nobody can read it. The dialog itself is replaced, so these
/// tests never open one.
/// </summary>
public class StartupErrorTests
{
    [Theory]
    [InlineData(0, 0u, true)]
    [InlineData(0, 2u, true)]
    [InlineData(0, 1u, false)]
    [InlineData(0, 3u, false)]
    [InlineData(0, 0x8003u, false)]
    [InlineData(1, 2u, false)]
    public void A_dialog_is_only_for_an_error_nobody_else_can_read(int window, uint stderrType, bool dialog) =>
        Assert.Equal(dialog, StartupError.NeedsDialog(window, stderrType));

    [Fact]
    public void No_console_asks_the_dialog_with_the_message_and_the_folder()
    {
        var seen = Asked(() => StartupError.Report("No shell.qml at C:\\missing", "C:\\configs", consoleWindow: 0, stderrType: 0));

        var call = Assert.Single(seen);
        Assert.Equal("No shell.qml at C:\\missing", call.Message);
        Assert.Equal("C:\\configs", call.Folder);
    }

    [Fact]
    public void A_console_does_not_ask_the_dialog()
    {
        var seen = Asked(() => StartupError.Report("No shell.qml at C:\\missing", "C:\\configs", consoleWindow: 1, stderrType: 2));

        Assert.Empty(seen);
    }

    [Fact]
    public void A_redirected_error_does_not_ask_the_dialog()
    {
        var seen = Asked(() => StartupError.Report("No config named \"work\"", "C:\\configs", consoleWindow: 0, stderrType: 3));

        Assert.Empty(seen);
    }

    [Fact]
    public void A_path_opens_that_folder_and_a_missing_root_opens_nothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "winshell-startup-error-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "shell.qml");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(root, StartupError.FolderFor(path, Path.Combine(root, "absent")));
            Assert.Equal(root, StartupError.FolderFor(null, root));
            Assert.Null(StartupError.FolderFor(null, Path.Combine(root, "absent")));
            Assert.Null(StartupError.FolderFor(Path.Combine(root, "gone", "shell.qml"), root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_dialog_struct_matches_the_packed_windows_layout()
    {
        if (nint.Size != 8)
            return;
        Assert.Equal(160, Marshal.SizeOf<StartupError.DialogConfig>());
        Assert.Equal(28, Marshal.OffsetOf<StartupError.DialogConfig>(nameof(StartupError.DialogConfig.pszWindowTitle)));
        Assert.Equal(64, Marshal.OffsetOf<StartupError.DialogConfig>(nameof(StartupError.DialogConfig.pButtons)));
        Assert.Equal(156, Marshal.OffsetOf<StartupError.DialogConfig>(nameof(StartupError.DialogConfig.cxWidth)));
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
