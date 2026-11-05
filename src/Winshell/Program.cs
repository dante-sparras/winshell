namespace Winshell;

internal static class Program
{
    // A startup error's dialog opens a folder through ShellExecute, which wants a single-threaded apartment. async Main
    // would lose it.
    [STAThread]
#pragma warning disable VSTHRD002 // Keep the STA thread; the commands await without a synchronization context.
    private static int Main(string[] args) => Cli.RunAsync(args).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
}
