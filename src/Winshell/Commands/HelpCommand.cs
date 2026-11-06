namespace Winshell.Commands;

internal static class HelpCommand
{
    internal static int Run(string? command = null)
    {
        Print(command);
        return 0;
    }

    internal static void Print(string? command)
    {
        Console.WriteLine(Text(command));
    }

    /// <summary>The help for <paramref name="command"/>, or the summary for null or a command with no help of its own.</summary>
    internal static string Text(string? command)
    {
        return CliParser.Help(command);
    }
}
