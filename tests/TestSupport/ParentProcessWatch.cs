using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        var status = NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, out var info, Marshal.SizeOf<BasicInformation>(), out _);
        if (status != 0)
            throw new Win32Exception(status, "NtQueryInformationProcess failed");
        try
        {
            return Process.GetProcessById((int)info.ParentProcessId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint ProcessId;
        public nint ParentProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(nint process, int informationClass, out BasicInformation information, int length, out int returned);
}
