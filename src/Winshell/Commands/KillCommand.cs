namespace Winshell.Commands;

internal static class KillCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        if (InstanceCall.Select(options, InstanceSelection.Question.Kill, out var code) is not { } info)
            return code;

        // Its supervisor must not start it again, even after a crash on the way out or the end --force gives it.
        var quit = await ShellExit.QuitAsync(info, ExitIntent.Stop, ShellExit.Patience.Interactive,
            options.Force ? ShellExit.Ending.Always : ShellExit.Ending.Never);
        if (!quit.Exited)
        {
            if (quit.Answer.Problem($"Instance {info.Id} refused") is { } problem)
                Console.Error.WriteLine(problem);
            Console.Error.WriteLine(options.Force
                ? $"Could not end instance {info.Id}."
                : $"Instance {info.Id} is still running. Use --force to end it.");
            return 1;
        }

        Console.WriteLine($"Killed {info.Id}");
        return 0;
    }
}
