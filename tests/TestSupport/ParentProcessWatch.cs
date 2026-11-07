using System.Diagnostics;
using System.Runtime.CompilerServices;
using static Winshell.Native.ProcessImage;

namespace Winshell.TestSupport;

/// <summary>
/// Ends a test program when the process that started it is gone. xUnit v3 runs tests in a program of its own under
/// testhost. A hang timeout that names no dump type kills only testhost, and the hung program then holds the pipe
/// that <c>dotnet test</c> writes to, so a run piped to anything never ends.
/// </summary>
internal static class ParentProcessWatch
{
    [ModuleInitializer]
    internal static void Start()
    {
        var parent = Parent();
        if (parent is null)
            return;
        new Thread(() =>
            {
                parent.WaitForExit();
                Process.GetCurrentProcess().Kill();
            })
            { IsBackground = true, Name = "Parent process watch" }.Start();
    }

    private static Process? Parent()
    {
        var id = ParentOf((uint)Environment.ProcessId) ??
                 throw new InvalidOperationException("This process's parent is not in the process snapshot.");
        try
        {
            return Process.GetProcessById((int)id);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
