using System.Globalization;

namespace Winshell;

/// <summary>Helpers the Windows service modules share.</summary>
internal static class ServiceSupport
{
    /// <summary>
    /// Runs background work and logs what it throws, so a failing Windows API does not take the shell down.
    /// </summary>
    public static void Run(string category, Func<Task> work) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A Windows service that fails is reported and the shell keeps going.
            catch (Exception e)
#pragma warning restore CA1031
            {
                Log.Default.Warn(category, e.Message);
            }
        });

    /// <summary>Releases a cancelled monitor's token source only after its task has ended, without waiting.</summary>
    public static void DisposeAfterMonitor(CancellationTokenSource stop, Task? monitor)
    {
        if (monitor is null || monitor.IsCompleted)
            stop.Dispose();
        else
            _ = monitor.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), stop,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>A MAC address as <c>XX:XX:XX:XX:XX:XX</c>.</summary>
    public static string FormatMac(ulong address)
    {
        return string.Join(':',
            Enumerable.Range(0, 6).Select(i =>
                ((address >> (8 * (5 - i))) & 0xff).ToString("X2", CultureInfo.InvariantCulture)));
    }

    public static string FormatMac(ReadOnlySpan<byte> bytes)
    {
        return string.Join(':', bytes.ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }
}
