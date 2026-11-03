using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Audio;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>Sound, as the volume slider, the output page and the app volume page show it.</summary>
public sealed class QuickSettingsSound : ObservableObject
{
    private readonly AudioService _audio;
    private AudioNode? _output;

    internal QuickSettingsSound(AudioService audio)
    {
        _audio = audio;
        audio.PropertyChanged += OnAudioService;
        audio.Nodes.CollectionChanged += OnAudioNodes;
        BindOutput();
        PublishOutputs();
        PublishSessions();
    }

    /// <summary>The default playback device's volume, or 0 without one.</summary>
    public double Volume { get; private set => Set(ref field, value); }

    public bool HasOutput { get; private set => Set(ref field, value); }

    public string MuteLabel { get; private set => Set(ref field, value); } = "Unmute";

    /// <summary>The playback devices, with the default chosen.</summary>
    public ObservableCollection<QuickSettingsChoice> Outputs { get; } = [];

    /// <summary>Each app's volume.</summary>
    public ObservableCollection<QuickSettingsSession> Sessions { get; } = [];

    public void SetVolume(double value)
    {
        var output = _audio.DefaultPlaybackDevice;
        if (output is null || value == output.Volume)
            return;
        output.Volume = value;
    }

    public void ToggleMute()
    {
        var output = _audio.DefaultPlaybackDevice;
        if (output is null)
            return;
        output.Muted = !output.Muted;
    }

    private void OnAudioService(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AudioService.DefaultPlaybackDevice))
            return;
        BindOutput();
        PublishOutputs();
    }

    private void OnAudioNodes(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PublishOutputs();
        PublishSessions();
    }

    private void OnOutputAudio(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioNode.Volume) or nameof(AudioNode.Muted))
            PublishVolume();
    }

    private void BindOutput()
    {
        var output = _audio.DefaultPlaybackDevice;
        if (ReferenceEquals(output, _output))
        {
            PublishVolume();
            return;
        }
        if (_output is not null)
            _output.PropertyChanged -= OnOutputAudio;
        _output = output;
        if (_output is not null)
            _output.PropertyChanged += OnOutputAudio;
        PublishVolume();
    }

    private void PublishVolume()
    {
        var audio = _output;
        Volume = audio?.Volume ?? 0;
        HasOutput = _output is not null;
        MuteLabel = audio is null || audio.Muted ? "Unmute" : "Mute";
    }

    private void PublishOutputs()
    {
        var nodes = _audio.Nodes.Where(node => node.IsPlayback && !node.IsSession).ToList();
        var chosen = _audio.DefaultPlaybackDevice;
        if (Outputs.Select(item => item.Node).SequenceEqual(nodes))
        {
            foreach (var item in Outputs)
                item.Chosen = ReferenceEquals(item.Node, chosen);
            return;
        }
        foreach (var item in Outputs)
            item.Release();
        Outputs.Clear();
        foreach (var node in nodes)
            Outputs.Add(new QuickSettingsChoice(node, node.Description, ReferenceEquals(node, chosen), () => _audio.DefaultPlaybackDevice = node));
    }

    private void PublishSessions()
    {
        var nodes = _audio.Nodes.Where(node => node.IsSession).ToList();
        if (Sessions.Select(session => session.Node).SequenceEqual(nodes))
            return;
        foreach (var session in Sessions)
            session.Release();
        Sessions.Clear();
        foreach (var node in nodes)
            Sessions.Add(new QuickSettingsSession(node));
    }
}
