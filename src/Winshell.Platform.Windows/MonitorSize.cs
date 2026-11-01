using System.Collections.Concurrent;
using Windows.Devices.Display;

namespace Winshell.Platform.Windows;

/// <summary>
/// A monitor's physical size in millimeters, read with the screen snapshot. The size is the EDID size from
/// <see cref="DisplayMonitor.PhysicalSizeInInches"/>. A projector or a remote session reports none, and then this is 0.
/// </summary>
internal static class MonitorSize
{
    private static readonly MonitorSizeCache Cache = new(Query, TimeSpan.FromMilliseconds(250));

    internal static (double WidthMm, double HeightMm) Read(string interfaceId) => Cache.Read(interfaceId);

    private static async Task<(double WidthMm, double HeightMm)> Query(string interfaceId)
    {
        try
        {
            var monitor = await DisplayMonitor.FromInterfaceIdAsync(interfaceId).AsTask().ConfigureAwait(false);
            var size = monitor?.PhysicalSizeInInches;
            return FromInches(size?.Width, size?.Height);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return (0, 0);
        }
    }

    /// <summary>Inches to millimeters. Either side missing or not positive is no size, so both are 0.</summary>
    internal static (double WidthMm, double HeightMm) FromInches(double? widthInches, double? heightInches)
    {
        if (widthInches is not > 0 || heightInches is not > 0)
            return (0, 0);
        return (widthInches.Value * 25.4, heightInches.Value * 25.4);
    }
}

/// <summary>
/// Asks Windows once for each monitor. The screen list is read on the loop whenever a window is placed, and a
/// monitor's size does not change, so only the first read waits, and a query that is late reports 0 until it answers.
/// </summary>
internal sealed class MonitorSizeCache(Func<string, Task<(double WidthMm, double HeightMm)>> query, TimeSpan wait)
{
    private readonly ConcurrentDictionary<string, Task<(double WidthMm, double HeightMm)>> _queries = new(StringComparer.Ordinal);

    internal (double WidthMm, double HeightMm) Read(string interfaceId)
    {
        if (string.IsNullOrEmpty(interfaceId))
            return (0, 0);
        if (_queries.TryGetValue(interfaceId, out var known))
            return known.IsCompletedSuccessfully ? known.Result : (0, 0);
        var started = _queries.GetOrAdd(interfaceId, query);
        return started.Wait(wait) ? started.Result : (0, 0);
    }
}
