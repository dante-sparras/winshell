using System.Globalization;
using System.Runtime.InteropServices;

namespace Winshell;

/// <summary>What this process holds, and the two calls that hand memory back to Windows (ADR 0062).</summary>
internal static class ShellMemory
{
    private const int HeapOptimizeResources = 3;
    private const uint HeapOptimizeResourcesVersion = 1;

    /// <summary>Bytes committed to this process alone. Zero when Windows does not say.</summary>
    public static long PrivateBytes() => Read(out var counters) ? (long)counters.PrivateUsage : 0;

    /// <summary>Bytes of this process that are in physical memory now. Zero when Windows does not say.</summary>
    public static long WorkingSetBytes() => Read(out var counters) ? (long)counters.WorkingSetSize : 0;

    /// <summary>Asks every native heap to give back the pages its freed blocks left behind.</summary>
    public static void ReleaseHeaps()
    {
        var information = new HeapOptimize { Version = HeapOptimizeResourcesVersion, Flags = 0 };
        _ = HeapSetInformation(0, HeapOptimizeResources, ref information, (nuint)Marshal.SizeOf<HeapOptimize>());
    }

    /// <summary>Takes every page of this process out of physical memory. A page comes back when it is next touched.</summary>
    public static void EmptyWorkingSet() => _ = K32EmptyWorkingSet(GetCurrentProcess());

    /// <summary>The processor time, thread count, and handle count of this process, as one log phrase.</summary>
    public static string Cost()
    {
        var process = GetCurrentProcess();
        var cpu = GetProcessTimes(process, out _, out _, out var kernel, out var user) ? (kernel + user) / 10_000 : 0;
        _ = GetProcessHandleCount(process, out var handles);
        int threads;
        using (var self = System.Diagnostics.Process.GetCurrentProcess())
        {
            threads = self.Threads.Count;
        }

        return "cpu=" + cpu.ToString(CultureInfo.InvariantCulture) + "ms threads=" + threads.ToString(CultureInfo.InvariantCulture)
            + " handles=" + handles.ToString(CultureInfo.InvariantCulture);
    }

    private static bool Read(out MemoryCounters counters)
    {
        counters = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
        return K32GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size);
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetProcessMemoryInfo(nint process, ref MemoryCounters counters, uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32EmptyWorkingSet(nint process);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HeapSetInformation(nint heap, int kind, ref HeapOptimize information, nuint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(nint process, out long created, out long exited, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessHandleCount(nint process, out uint count);

    [StructLayout(LayoutKind.Sequential)]
    private struct HeapOptimize
    {
        public uint Version;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }
}
