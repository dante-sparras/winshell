using System.ComponentModel;
using Winshell.Audio;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>One app's volume. Mute and the slider change this session only.</summary>
public sealed class QuickSettingsSession : ObservableObject
{
    private readonly AudioNode _node;

    internal QuickSettingsSession(AudioNode node)
    {
        _node = node;
        node.PropertyChanged += OnNode;
        node.PropertyChanged += OnAudio;
    }

    public AudioNode Node => _node;

    public string Label => _node.Description;

    public double Volume => _node.Volume;

    public string Glyph => StatusGlyphs.VolumeGlyph(_node.Muted, _node.Volume);

    public string MuteLabel => _node.Muted ? "Unmute" : "Mute";

    public void SetVolume(double value)
    {
        if (value == _node.Volume)
            return;
        _node.Volume = value;
    }

    public void ToggleMute() => _node.Muted = !_node.Muted;

    internal void Release()
    {
        _node.PropertyChanged -= OnNode;
        _node.PropertyChanged -= OnAudio;
    }

    private void OnNode(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioNode.Description))
            Changed(nameof(Label));
    }

    private void OnAudio(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioNode.Volume))
        {
            Changed(nameof(Volume));
            Changed(nameof(Glyph));
        }
        else if (e.PropertyName == nameof(AudioNode.Muted))
        {
            Changed(nameof(Glyph));
            Changed(nameof(MuteLabel));
        }
    }
}
