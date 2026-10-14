using System.Globalization;
using System.Runtime;

namespace Winshell;

/// <summary>
/// Gives memory back while the shell is idle (ADR 0062). The gate in <see cref="IdleTrimGate"/> decides.
/// A sweep collects the young generations. A trim compacts and returns native pages. A lock also empties the working set.
/// </summary>
internal sealed class ShellTrim
{
    /// <summary>One sweep in this many writes a log line. A sweep can run twice a minute all day.</summary>
    private const int SweepsPerLine = 10;

    private readonly IdleTrimGate _gate = new();
    private readonly object _work = new();
    private int _running;
    private bool _logged;
    private int _sweeps;
    private long _loggedAt = Environment.TickCount64;
    private long _loggedAllocated;

    /// <summary>Runs a trim or a sweep off the shell's thread when one is due.</summary>
    /// <param name="busy">True while a panel is open, a recount is pending, or a plugin holds Escape.</param>
    public void Consider(bool busy)
    {
        if (Volatile.Read(ref _running) == 1)
        {
            return;
        }

        var now = Environment.TickCount64;
        var allocated = GC.GetTotalAllocatedBytes();
        bool trim;
        lock (_gate)
        {
            trim = _gate.ShouldTrim(now, ShellWin32.IdleSeconds(), busy, allocated);
            if (!trim && !_gate.ShouldSweep(now, busy, allocated, GC.CollectionCount(0)))
            {
                return;
            }
        }

        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                if (trim)
                {
                    Trim(unseen: false);
                }
                else
                {
                    Sweep();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }).Forget("Bar", trim ? "idle trim" : "sweep");
    }

    /// <summary>
    /// The session locked or the display turned off. Trims at once, whatever is open, and then empties the working set.
    /// Safe from any thread.
    /// </summary>
    public void Unseen()
    {
        lock (_gate)
        {
            _gate.Trimmed(Environment.TickCount64, GC.GetTotalAllocatedBytes());
        }

        Task.Run(() => Trim(unseen: true)).Forget("Bar", "trim at lock");
    }

    private void Trim(bool unseen)
    {
        lock (_work)
        {
            var heapBefore = Committed();
            var privateBefore = ShellMemory.PrivateBytes();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            var collected = GC.GetGCMemoryInfo();
            GC.WaitForPendingFinalizers();
            ShellMemory.ReleaseHeaps();
            var line = (unseen ? "trim at lock: " : "idle trim: ") + Figures(heapBefore, privateBefore, collected);
            if (unseen)
            {
                var held = ShellMemory.WorkingSetBytes();
                ShellMemory.EmptyWorkingSet();
                line += ", working set " + Megabytes(held) + " MB emptied";
            }

            if (_logged)
            {
                Log.Debug("Bar", line);
                return;
            }

            _logged = true;
            Log.Information("Bar", line + ", " + ShellMemory.Cost());
        }
    }

    private void Sweep()
    {
        lock (_work)
        {
            var line = ++_sweeps % SweepsPerLine == 1;
            var heapBefore = line ? Committed() : 0;
            var privateBefore = line ? ShellMemory.PrivateBytes() : 0;
            GC.Collect(1, GCCollectionMode.Forced, blocking: true, compacting: false);
            var collected = line ? GC.GetGCMemoryInfo(GCKind.Any) : default;
            GC.WaitForPendingFinalizers();
            if (line)
            {
                Log.Debug("Bar", "sweep " + _sweeps.ToString(CultureInfo.InvariantCulture) + ": " + Figures(heapBefore, privateBefore, collected));
            }
        }
    }

    private string Figures(long heapBefore, long privateBefore, GCMemoryInfo collected)
    {
        var now = Environment.TickCount64;
        var allocated = GC.GetTotalAllocatedBytes();
        var pause = collected.PauseDurations.Length > 0 ? collected.PauseDurations[0].TotalMilliseconds : 0;
        var line = "allocated " + Megabytes(allocated - _loggedAllocated) + " MB in " + ((now - _loggedAt) / 1000).ToString(CultureInfo.InvariantCulture)
            + " s, managed heap " + Megabytes(heapBefore) + " to " + Megabytes(collected.TotalCommittedBytes) + " MB committed, finalizers "
            + collected.FinalizationPendingCount.ToString(CultureInfo.InvariantCulture) + ", private " + Megabytes(privateBefore) + " to "
            + Megabytes(ShellMemory.PrivateBytes()) + " MB, pause " + pause.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
        _loggedAt = now;
        _loggedAllocated = allocated;
        return line;
    }

    private static long Committed() =>
        GC.GetGCMemoryInfo(GCKind.Any).TotalCommittedBytes is > 0 and var committed ? committed : GC.GetTotalMemory(forceFullCollection: false);

    private static string Megabytes(long bytes) => (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture);
}
