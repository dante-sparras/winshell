using Winshell;
using Winshell.DefaultShell;

internal static class Program
{
    [STAThread]
    private static void Main() => Shell.Run<DefaultShellConfiguration>(new ShellOptions { ShellId = "winshell-default" });
}
