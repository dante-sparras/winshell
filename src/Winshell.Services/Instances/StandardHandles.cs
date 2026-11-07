using Winshell.Native;

namespace Winshell;

/// <summary>This process's standard input, output and error, as the processes it starts see them.</summary>
internal static class StandardHandles
{
    /// <summary>
    /// Keeps this process's standard handles out of every process it starts from now on. A child gets a copy of every
    /// handle that can be inherited, so a shell that a <c>-d</c> start leaves behind would hold its caller's pipes open
    /// until it exits, and <c>out=$(winshell run -d)</c> would wait that long.
    /// </summary>
    public static void KeepFromChildren()
    {
        ProcessConsole.KeepFromChildren(StandardStream.Input);
        ProcessConsole.KeepFromChildren(StandardStream.Output);
        ProcessConsole.KeepFromChildren(StandardStream.Error);
    }
}
