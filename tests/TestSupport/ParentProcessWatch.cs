using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;

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
        try
        {
            return Process.GetProcessById((int)ParentId());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static uint ParentId()
    {
        var snapshot = PInvoke.CreateToolhelp32Snapshot(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot == new HANDLE(-1))
            throw new Win32Exception();
        try
        {
            var self = (uint)Environment.ProcessId;
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            for (var more = PInvoke.Process32FirstW(snapshot, ref entry);
                 more;
                 more = PInvoke.Process32NextW(snapshot, ref entry))
                if (entry.th32ProcessID == self)
                    return entry.th32ParentProcessID;
            throw new InvalidOperationException("This process is not in the process snapshot.");
        }
        finally
        {
            PInvoke.CloseHandle(snapshot);
        }
    }
}
