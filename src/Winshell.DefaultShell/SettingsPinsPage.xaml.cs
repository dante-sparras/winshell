using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>Start's pins, in Start's order, to move or unpin. They are the pins Start already keeps.</summary>
public sealed partial class SettingsPinsPage : UserControl
{
    private readonly Start _start;

    public SettingsPinsPage(Start start)
    {
        _start = start;
        InitializeComponent();
        Pins.RemoveLabel = "Unpin";
        Pins.RemoveGlyph = "\uE77A";
        Pins.EmptyText = "Nothing pinned";
        Pins.MoveRequested += OnMove;
        Pins.RemoveRequested += OnRemove;
        start.Updated += (_, _) => Show();
        Show();
    }

    private void OnMove(object? sender, SettingsOrderMove move)
    {
        _start.MoveShown(move.Key, move.Offset);
        Show();
        Pins.Focus(move.Key, move.Button);
    }

    private void OnRemove(object? sender, string key)
    {
        var shown = ShownIds();
        var index = IndexOf(shown, key);
        _start.UnpinShown(key);
        Show();
        var next = SettingsOrder.FocusIndex(ShownIds(), index);
        if (next is not null)
            Pins.Focus(next, "remove");
    }

    private void Show()
    {
        var items = new List<SettingsOrderItem>(_start.Pinned.Count);
        foreach (var app in _start.Pinned)
            items.Add(new SettingsOrderItem(app.Id, app.Name, Icon: app.Icon));
        Pins.Show(items);
    }

    private List<string> ShownIds()
    {
        var ids = new List<string>(_start.Pinned.Count);
        foreach (var app in _start.Pinned)
            ids.Add(app.Id);
        return ids;
    }

    private static int IndexOf(List<string> ids, string key)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (StartList.SameId(ids[i], key))
                return i;
        }
        return 0;
    }
}
