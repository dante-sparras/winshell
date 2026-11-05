using Winshell.Native;

namespace Winshell;

/// <summary>This process's standard input, output and error, as the processes it starts see them.</summary>
internal static class StandardHandles
{
    /// <summary>This process's standard error: 0 when it has none, and -1 when Windows could not say.</summary>
    public static nint Error => ProcessConsole.StandardHandle(StandardStream.Error);

    /// <summary>
    /// Keeps this process's standard handles out of every process it starts from now on. A child gets a copy of every
    /// handle that can be inherited, so a shell that a <c>-d</c> start leaves behind would hold its caller's pipes open
    /// until it exits, and <c>out=$(winshell run -d)</c> would wait that long.
    /// </summary>
    public static void KeepFromChildren()
    {
        foreach (var stream in (ReadOnlySpan<StandardStream>)[StandardStream.Input, StandardStream.Output, StandardStream.Error])
        {
            var handle = ProcessConsole.StandardHandle(stream);
            // 0 is no such handle, and -1 a call that failed.
            if (handle is not (0 or -1))
                _ = ProcessConsole.KeepFromChildren(handle);
        }
    }
}
