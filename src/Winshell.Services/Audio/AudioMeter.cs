namespace Winshell.Audio;

/// <summary>The peak meter of a device or session.</summary>
internal interface IAudioMeterInformation
{
    /// <summary>The peak of each channel since the last read, from 0 to 1. Empty when Windows has no reading.</summary>
    float[] ReadPeaks();
}

/// <summary>The Windows peak meter of a device or session. It does not own the COM object.</summary>
internal sealed class WindowsAudioMeter(Native.IAudioMeterInformation meter) : IAudioMeterInformation
{
    public float[] ReadPeaks()
    {
        meter.GetMeteringChannelCount(out var count);
        var peaks = new float[count];
        return count > 0 && meter.GetChannelsPeakValues(count, peaks) == 0 ? peaks : [];
    }
}
