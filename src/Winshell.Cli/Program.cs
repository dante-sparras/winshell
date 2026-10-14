using Winshell.Core;

namespace Winshell.Cli;

/// <summary>Asks the running shell. Like <c>qs ipc</c>, it starts, prints, and leaves.</summary>
public static class Program
{
    /// <summary>Runs one line.</summary>
    /// <param name="args">The words after <c>winshell</c>.</param>
    /// <returns>0 when the shell answered. 1 when the line was refused or the shell is absent.</returns>
    public static int Main(string[] args) =>
        ShellClient.Ask(ShellPipe.Name, string.Join(' ', args), Console.Out, Console.Error);
}
