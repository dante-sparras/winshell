using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>Io members from issue 137 report the real failure or fail to load.</summary>
[Collection(ThemeCollection.Name)]
public class IoMemberTests
{
    [Fact]
    public void Wait_for_job_is_not_a_member()
    {
        using var shell = new TestShell(("shell.qml", """
            import Winshell.Io
            FileView { waitForJob: true }
            """));
        var ex = Assert.Throws<QmlCompileException>(() => shell.Load());
        Assert.Contains("waitForJob", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_only_file_reports_permission_denied()
    {
        using var shell = new TestShell(("shell.qml", View("Shell.shellDir + \"/note.txt\"")), ("note.txt", "old"));
        var root = shell.Load();
        var path = Path.Combine(shell.Directory, "note.txt");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            shell.Eval(root, "view.setText('new')");
            shell.Loop.Process();
            Assert.Equal((int)FileView.FileViewError.PermissionDenied, Code(shell, root));
            Assert.Equal("old", File.ReadAllText(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void A_folder_reports_that_it_is_not_a_file()
    {
        using var shell = new TestShell(("shell.qml", View("Shell.shellDir")));
        var root = shell.Load();
        shell.Eval(root, "view.setText('new')");
        shell.Loop.Process();
        Assert.Equal((int)FileView.FileViewError.NotAFile, Code(shell, root));
    }

    [Fact]
    public async Task A_folder_path_is_an_unknown_socket_error()
    {
        using var shell = new TestShell();
        var folder = Path.Combine(shell.Directory, "folder");
        Directory.CreateDirectory(folder);
        Assert.Equal((int)SocketObject.SocketError.Unknown, await ConnectError(shell, folder));
    }

    [Fact]
    public async Task A_missing_socket_is_refused()
    {
        using var shell = new TestShell();
        var missing = Path.Combine(shell.Directory, "nope.sock");
        Assert.Equal((int)SocketObject.SocketError.ConnectionRefused, await ConnectError(shell, missing));
    }

    private static string View(string path) => $$"""
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias view: file
          property int failed: -1
          FileView {
            id: file
            path: {{path}}
            blockLoading: true
            blockWrites: true
            printErrors: false
            onSaveFailed: function(error) { failed = error }
          }
        }
        """;

    private static int Code(TestShell shell, QObject root) =>
        Convert.ToInt32(shell.Eval(root, "failed"), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<int> ConnectError(TestShell shell, string path)
    {
        var socket = new SocketObject { Engine = shell.Engine, Path = path };
        int? error = null;
        socket.Connect("error", args => error = Convert.ToInt32(args[0], System.Globalization.CultureInfo.InvariantCulture));
        socket.Connected = true;
        await Wait.UntilAsync(() => error is not null, "the connection to fail", shell.Loop.Process);
        return error!.Value;
    }
}
