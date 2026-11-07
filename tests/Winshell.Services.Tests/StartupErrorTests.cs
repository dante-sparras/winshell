using Winshell.Native;

namespace Winshell.Services.Tests;

/// <summary>A pre-start failure is a dialog only when nobody can read it, and the dialog opens the folder to fix.</summary>
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
}
