namespace Winshell;

internal static class Program
{
    // The default shell runs WinUI on this entry thread; async Main would lose its STA apartment.
    [STAThread]
#pragma warning disable VSTHRD002 // Keep the shell on the STA thread; other commands await without a synchronization context.
    private static int Main(string[] args) => Cli.RunAsync(args).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
}
