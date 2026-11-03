using System.ComponentModel;
using Winshell.Audio;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>A power mode or a playback device on a page of choices.</summary>
public sealed class QuickSettingsChoice : ObservableObject
{
    private readonly Action _choose;

    internal QuickSettingsChoice(AudioNode? node, string text, bool chosen, Action choose)
    {
        Node = node;
        Text = text;
        Chosen = chosen;
        _choose = choose;
        if (node is not null)
            node.PropertyChanged += OnNode;
    }

    public AudioNode? Node { get; }

    public string Text { get; private set => Set(ref field, value); }

    public bool Chosen { get; internal set => Set(ref field, value); }

    public void Choose() => _choose();

    internal void Release()
    {
        if (Node is not null)
            Node.PropertyChanged -= OnNode;
    }

    private void OnNode(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioNode.Description) && Node is not null)
            Text = Node.Description;
    }
}
