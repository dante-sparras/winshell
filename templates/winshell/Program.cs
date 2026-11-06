using MyShell;
using Winshell;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Shell.Run<AppShell>(new ShellOptions { ShellId = "MyShell" });
    }
}
