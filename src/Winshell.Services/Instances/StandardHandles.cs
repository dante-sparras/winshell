using System.Runtime.InteropServices;

namespace Winshell;

/// <summary>This process's standard input, output and error, as the processes it starts see them.</summary>
internal static partial class StandardHandles
{
    private const int StandardInput = -10;
    private const int StandardOutput = -11;
    private const int StandardError = -12;
    private const uint HandleFlagInherit = 1;

    /// <summary>This process's standard error: 0 when it has none, and -1 when Windows could not say.</summary>
    public static nint Error => GetStdHandle(StandardError);

    /// <summary>
    /// Keeps this process's standard handles out of every process it starts from now on. A child gets a copy of every
    /// handle that can be inherited, so a shell that a <c>-d</c> start leaves behind would hold its caller's pipes open
    /// until it exits, and <c>out=$(winshell run -d)</c> would wait that long.
    /// </summary>
    public static void KeepFromChildren()
    {
        foreach (var which in (ReadOnlySpan<int>)[StandardInput, StandardOutput, StandardError])
        {
            var handle = GetStdHandle(which);
            // 0 is no such handle, and -1 a call that failed.
            if (handle is not (0 or -1))
                _ = SetHandleInformation(handle, HandleFlagInherit, 0);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int which);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint handle, uint mask, uint flags);
}
