#pragma warning disable CA1416
using System.Runtime.InteropServices;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A save follows a symbolic link and writes through a hard link instead of replacing the name.</summary>
public class FileViewLinkTests
{
    private const string Qml = """
        import Winshell
        import Winshell.Io
        import QtQuick
        Scope {
          property alias view: file
          FileView {
            id: file
            path: Shell.shellDir + "/link.toml"
            blockLoading: true
            blockWrites: true
            printErrors: false
          }
        }
        """;

    [Fact]
    public void A_save_updates_the_symbolic_link_target_and_keeps_the_link()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var real = Path.Combine(shell.Directory, "real.toml");
        var link = Path.Combine(shell.Directory, "link.toml");
        File.WriteAllText(real, "old\n");
        try
        {
            File.CreateSymbolicLink(link, real);
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) == 1314)
        {
            // ERROR_PRIVILEGE_NOT_HELD. This process cannot create a symbolic link, so the assertion
            // runs only where that privilege is held. The hard-link test covers the in-place path.
            return;
        }
        var root = shell.Load();

        shell.Eval(root, "view.setText('kept')");

        Assert.Contains("kept", File.ReadAllText(real), StringComparison.Ordinal);
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Contains("kept", File.ReadAllText(link), StringComparison.Ordinal);
    }

    [Fact]
    public void A_save_updates_a_hard_link_without_replacing_the_name()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var real = Path.Combine(shell.Directory, "real.toml");
        var link = Path.Combine(shell.Directory, "link.toml");
        File.WriteAllText(real, "old\n");
        if (!CreateHardLinkW(link, real, IntPtr.Zero))
            throw new InvalidOperationException("CreateHardLinkW failed: " + Marshal.GetLastWin32Error());
        var root = shell.Load();

        shell.Eval(root, "view.setText('kept')");

        Assert.Contains("kept", File.ReadAllText(real), StringComparison.Ordinal);
        Assert.Contains("kept", File.ReadAllText(link), StringComparison.Ordinal);
        Assert.True(FileView.SaveTarget(link, out var hardLink).Length > 0);
        Assert.True(hardLink);
    }

    [Fact]
    public void A_second_save_through_a_hard_link_updates_both_names()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var real = Path.Combine(shell.Directory, "real.toml");
        var link = Path.Combine(shell.Directory, "link.toml");
        File.WriteAllText(real, "old\n");
        if (!CreateHardLinkW(link, real, IntPtr.Zero))
            throw new InvalidOperationException("CreateHardLinkW failed: " + Marshal.GetLastWin32Error());
        var root = shell.Load();

        shell.Eval(root, "view.setText('one')");
        shell.Eval(root, "view.setText('two')");

        Assert.Contains("two", File.ReadAllText(real), StringComparison.Ordinal);
        Assert.Contains("two", File.ReadAllText(link), StringComparison.Ordinal);
        Assert.True(FileView.SaveTarget(link, out var hardLink).Length > 0);
        Assert.True(hardLink);
    }

    [Fact]
    public void A_second_save_through_a_symbolic_link_keeps_the_link()
    {
        using var shell = new TestShell(("shell.qml", Qml));
        var real = Path.Combine(shell.Directory, "real.toml");
        var link = Path.Combine(shell.Directory, "link.toml");
        File.WriteAllText(real, "old\n");
        try
        {
            File.CreateSymbolicLink(link, real);
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) == 1314)
        {
            return;
        }
        var root = shell.Load();

        shell.Eval(root, "view.setText('one')");
        shell.Eval(root, "view.setText('two')");

        Assert.Contains("two", File.ReadAllText(real), StringComparison.Ordinal);
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Contains("two", File.ReadAllText(link), StringComparison.Ordinal);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);
}
