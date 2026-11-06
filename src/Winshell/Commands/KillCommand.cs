namespace Winshell.Commands;

internal static class KillCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        var choice = InstanceSelection.Select(options);
        if (choice.Info is not { } info)
        {
            Console.Error.WriteLine(choice.Error == InstanceSelection.NoneRunning
                ? InstanceSelection.NoneToKill
                : choice.Error);
            return choice.Code;
        }

        // Its supervisor must not start it again, even after a crash on the way out or the end --force gives it.
        var quit = await ShellExit.QuitAsync(info, ExitIntent.Stop, ShellExit.Patience.Interactive,
            options.Force ? ShellExit.Ending.Always : ShellExit.Ending.Never);
        if (!quit.Exited)
        {
            var failure = quit.Answer.Response is { Ok: false } refused
                ? $"Instance {info.Id} refused: {refused.Error}"
                : quit.Answer.Failure;
            if (failure is not null)
                Console.Error.WriteLine(failure);
            Console.Error.WriteLine(options.Force
                ? $"Could not end instance {info.Id}."
                : $"Instance {info.Id} is still running. Use --force to end it.");
            return 1;
        }

        Console.WriteLine($"Killed {info.Id}");
        return 0;
    }
}
