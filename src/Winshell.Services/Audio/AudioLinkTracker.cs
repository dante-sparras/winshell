using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Winshell.Audio;

/// <summary>The link groups connected to one node: the apps playing to a speaker, or the speaker one app plays to.</summary>
public sealed class AudioLinkTracker : ObservableObject, IDisposable
{
    private AudioNode? _node;

    /// <summary>The node to track. It becomes null when the node is removed.</summary>
    public AudioNode? Node
    {
        get => _node;
        set
        {
            if (_node == value)
                return;
            Detach();
            _node = value;
            if (value is not null)
            {
                value.Service.LinkGroups.CollectionChanged += OnLinkGroupsChanged;
                value.Removed += OnNodeRemoved;
            }
            Changed();
            Update();
        }
    }

    public ObservableCollection<AudioLinkGroup> LinkGroups { get; } = [];

    private void OnLinkGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    private void OnNodeRemoved() => Node = null;

    private void Update()
    {
        var node = _node;
        var groups = node?.Service.LinkGroups.Where(g => g.Source == node || g.Target == node) ?? [];
        LinkGroups.Sync(groups, g => g, g => g, g => g, static (_, _) => { });
    }

    private void Detach()
    {
        if (_node is null)
            return;
        _node.Service.LinkGroups.CollectionChanged -= OnLinkGroupsChanged;
        _node.Removed -= OnNodeRemoved;
    }

    public void Dispose() => Node = null;
}
