namespace Winshell.Commands;

internal static class MsgCommand
{
    internal static Task<int> RunAsync(CliOptions options)
    {
        Console.Error.WriteLine("winshell msg is deprecated; use winshell ipc call.");
        return IpcCommand.RunAsync(options with { Positional = ["call", .. options.Positional] });
    }
}
