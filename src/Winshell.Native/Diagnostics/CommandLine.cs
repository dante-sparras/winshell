using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>A command line split into arguments as Windows splits it.</summary>
internal static unsafe class CommandLine
{
    /// <summary>The arguments, or none for a blank line: <c>CommandLineToArgvW</c> returns this program's path for one.</summary>
    internal static string[] Split(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return [];
        var argv = Win32.CommandLineToArgvW(command, out var count);
        if (argv == 0)
            return [];
        try
        {
            var args = new string[count];
            for (var i = 0; i < count; i++)
                args[i] = Marshal.PtrToStringUni(((nint*)argv)[i]) ?? "";
            return args;
        }
        finally
        {
            Win32.LocalFree(argv);
        }
    }
}
