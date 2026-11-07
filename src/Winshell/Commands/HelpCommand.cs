namespace Winshell.Commands;

internal static class HelpCommand
{
    /// <summary>Prints the help for <paramref name="command"/>, or the summary for null or a command with no help of its own.</summary>
    internal static int Run(string? command = null)
    {
        Console.WriteLine(CliParser.Help(command));
        return 0;
    }
}
