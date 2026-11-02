namespace Winshell;

/// <summary>
/// Whether this shell may start over so an update can replace its files. Another running Winshell would keep those
/// files. The next process is this one's arguments, with <c>--after</c> naming this process, as a reload's handoff is.
/// </summary>
internal static class UpdateRestart
{
    public const string AnotherRunning = "Another Winshell is running. Close it, then restart this one.";

    public static string? Decide(ShellArguments arguments, int pid, bool anotherRunning, out IReadOnlyList<string> next)
    {
        next = arguments.ForNext(pid);
        return anotherRunning ? AnotherRunning : null;
    }
}
