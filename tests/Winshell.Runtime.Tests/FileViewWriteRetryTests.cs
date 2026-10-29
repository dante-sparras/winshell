using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A file lock that clears is not a lost save. A lock that stays is reported.</summary>
public class FileViewWriteRetryTests
{
    private const string Qml = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias view: file
          property int failed: 0
          FileView {
            id: file
            path: Shell.shellDir + "/settings.toml"
            blockLoading: true
            blockWrites: true
            printErrors: false
            onSaveFailed: function(error) { failed = failed + 1 }
          }
        }
        """;

    [Fact]
    public void A_lock_that_clears_does_not_lose_the_save()
    {
        using var shell = new TestShell(("shell.qml", Qml), ("settings.toml", "old\n"));
        var root = shell.Load();
        var path = Path.Combine(shell.Directory, "settings.toml");
        var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var releaser = new Thread(() =>
        {
            Thread.Sleep(40);
            locked.Dispose();
        });
        releaser.Start();

        shell.Eval(root, "view.setText('kept')");
        releaser.Join();
        shell.Loop.Process();

        Assert.Equal(0, Convert.ToInt32(shell.Eval(root, "failed"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("kept", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lock_that_stays_reports_the_failed_save()
    {
        using var shell = new TestShell(("shell.qml", Qml), ("settings.toml", "old\n"));
        var root = shell.Load();
        var path = Path.Combine(shell.Directory, "settings.toml");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        shell.Eval(root, "view.setText('kept')");
        shell.Loop.Process();

        Assert.Equal(1, Convert.ToInt32(shell.Eval(root, "failed"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("old\n", File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
