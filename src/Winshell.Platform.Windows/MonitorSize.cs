using Windows.Devices.Display;

namespace Winshell.Platform.Windows;

/// <summary>
/// A monitor's physical size in millimeters, read with the screen snapshot. The size is the EDID size from
/// <see cref="DisplayMonitor.PhysicalSizeInInches"/>. A projector or a remote session reports none, and then this is 0.
/// </summary>
internal static class MonitorSize
{
    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(250);

    internal static (double WidthMm, double HeightMm) Read(string interfaceId)
    {
        if (string.IsNullOrEmpty(interfaceId))
            return (0, 0);
        // A test synchronization context would resume this call on the thread that is waiting for it.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var task = DisplayMonitor.FromInterfaceIdAsync(interfaceId).AsTask();
            if (!task.Wait(Wait))
            {
                task.ContinueWith(static t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                return (0, 0);
            }
            var size = task.GetAwaiter().GetResult()?.PhysicalSizeInInches;
            return FromInches(size?.Width, size?.Height);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return (0, 0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
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
