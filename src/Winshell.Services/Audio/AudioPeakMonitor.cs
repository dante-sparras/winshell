using System.Runtime.InteropServices;

namespace Winshell.Audio;

/// <summary>
/// The peak level of a node's channels, read from the Windows peak meter about 30 times a second while enabled.
/// Windows meters a device after its volume is applied. A recording device only meters while something records from
/// it, so this keeps a recording stream open on it, and Windows shows its microphone privacy indicator meanwhile.
/// </summary>
public sealed class AudioPeakMonitor : ObservableObject, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(33);

    private readonly IEventLoop _loop;
    private AudioNode? _node;
    private bool _enabled = true;
    private double _peak;
    private double[] _peaks = [];
    private AudioChannel[] _channels = [];
    private bool _disposed;
    private IDisposable? _timer;
    private IDisposable? _keepRunning;
    private IAudioMeterInformation? _meter;

    public AudioPeakMonitor(IEventLoop loop) => _loop = loop;

    public AudioNode? Node
    {
        get => _node;
        set
        {
            if (_node == value)
                return;
            _node?.Removed -= Restart;
            value?.Removed += Restart;
            Set(ref _node, value);
            Restart();
        }
    }

    /// <summary>Whether the monitor reads the meter. While it is false, <see cref="Peak"/> is 0 and the lists are empty.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
                Restart();
        }
    }

    /// <summary>The highest of <see cref="Peaks"/>, from 0 to 1.</summary>
    public double Peak => _peak;

    /// <summary>The peak of each channel since the last read, from 0 to 1.</summary>
    public IReadOnlyList<double> Peaks => _peaks;

    /// <summary>The speaker position of each entry of <see cref="Peaks"/>.</summary>
    public IReadOnlyList<AudioChannel> Channels => _channels;

    private void Restart()
    {
        Stop();
        _meter = !_disposed && _enabled && _node is { IsRemoved: false } ? _node.Node.Meter : null;
        if (_meter is null)
        {
            Clear();
            return;
        }
        _keepRunning = _node!.Node.KeepRunning();
        Tick();
    }

    private void Tick()
    {
        _timer = null;
        if (_meter is not { } meter)
            return;
        try
        {
            meter.GetMeteringChannelCount(out var count);
            var values = new float[count];
            if (count > 0 && meter.GetChannelsPeakValues(count, values) == 0)
                Apply(values);
        }
        catch (COMException)
        {
            // The device went away; the node is removed shortly.
        }
        // A handler of the change above can have stopped or restarted the monitor.
        if (ReferenceEquals(_meter, meter) && _timer is null)
            _timer = _loop.Schedule(Interval, Tick);
    }

    private void Apply(float[] values)
    {
        var peaks = values.Select(v => (double)v).ToArray();
        if (!peaks.SequenceEqual(_peaks))
        {
            _peaks = peaks;
            Changed(nameof(Peaks));
        }
        Set(ref _peak, values.Length == 0 ? 0 : values.Max(), nameof(Peak));
        var node = _node!.Node;
        var channels = (node.Channels.Length == values.Length ? node.Channels : ChannelLayout.FromFormat(null, values.Length)).Select(c => (AudioChannel)c).ToArray();
        if (!channels.SequenceEqual(_channels))
        {
            _channels = channels;
            Changed(nameof(Channels));
        }
    }

    private void Clear()
    {
        if (_peaks.Length > 0)
        {
            _peaks = [];
            Changed(nameof(Peaks));
        }
        if (_channels.Length > 0)
        {
            _channels = [];
            Changed(nameof(Channels));
        }
        Set(ref _peak, 0, nameof(Peak));
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _keepRunning?.Dispose();
        _keepRunning = null;
        _meter = null;
    }

    public void Dispose()
    {
        _disposed = true;
        Node = null;
    }
}
