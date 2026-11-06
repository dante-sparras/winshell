using System.Diagnostics;
using System.Text;
using Winshell.Commands;

namespace Winshell;

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        WriteUtf8WhenRedirected();
        CliOptions options;
        try
        {
            options = CliParser.Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }

        var command = options.Command;
        if (options.ShowHelp)
            return HelpCommand.Run(command);
        if (options.ShowVersion)
        {
            Console.WriteLine($"winshell {CliMetadata.Version}");
            return 0;
        }

        // Commands report failures as exit 1. The shell reports its own crashes.
        try
        {
            switch (command)
            {
                case null:
                    // The Start tile and sign-in host still start a shell without a command word.
                    if (options.Arguments.IsEmpty && !IsWindowlessHost())
                        return HelpCommand.Run();
                    if (!IsWindowlessHost() && !options.ShellStartAllowed)
                    {
                        Console.Error.WriteLine("No command. Use winshell run to start a shell.");
                        return 2;
                    }

                    return RunCommand.Run(options);
                case "run":
                    return RunCommand.Run(options);
                case "list":
                    return ListCommand.Run(options);
                case "kill":
                    return await KillCommand.RunAsync(options);
                case "reload":
                    return await ReloadCommand.RunAsync(options);
                case "log":
                    return await LogCommand.RunAsync(options);
                case "ipc":
                    return await IpcCommand.RunAsync(options);
                case "doctor":
                    return DoctorCommand.Run(options);
                case "inspect":
                    return await InspectCommand.RunAsync(options);
                case "dev":
                    return await DevCommand.RunAsync(options);
                default:
                    // The command table lists every case here, and refuses any other.
                    throw new UnreachableException($"No command \"{command}\".");
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException && command is not (null or "run"))
        {
            Console.Error.WriteLine(options.Verbose ? $"winshell {command}: {e}" : $"winshell {command}: {e.Message}");
            return 1;
        }
    }

    /// <summary><c>winshellw.exe</c> is the same program with no console. It still starts a shell when given no command.</summary>
    private static bool IsWindowlessHost()
    {
        return string.Equals(Path.GetFileName(Environment.ProcessPath), "winshellw.exe",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A pipe or a file gets UTF-8. The console's own code page would turn text outside it into question marks,
    /// and setting that code page would change it for the terminal too.
    /// </summary>
    private static void WriteUtf8WhenRedirected()
    {
        var utf8 = new UTF8Encoding(false);
        if (Console.IsOutputRedirected)
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        if (Console.IsErrorRedirected)
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
    }
}
