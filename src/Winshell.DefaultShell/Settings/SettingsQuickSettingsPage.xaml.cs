using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The quick settings tiles in order. The layout is the one quick settings already follows.</summary>
public sealed partial class SettingsQuickSettingsPage : UserControl
{
    private readonly QuickSettingsLayout _layout;
    private readonly Dictionary<string, Button> _add = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _keys = [];

    public SettingsQuickSettingsPage(QuickSettingsLayout layout)
    {
        _layout = layout;
        InitializeComponent();
        TilesCard.Apply(SettingsCatalog.RequireSetting("quickSettingsTiles"));
        TilesCard.IsTabStop = true;
        Tiles.EmptyText = "Quick settings shows no tiles";
        Tiles.MoveRequested += OnMove;
        Tiles.RemoveRequested += OnRemove;
        layout.PropertyChanged += OnLayout;
        Show();
    }

    public bool FocusSetting(string key)
    {
        if (key != "quickSettingsTiles")
            return false;
        TilesCard.Focus(FocusState.Programmatic);
        TilesCard.StartBringIntoView();
        return true;
    }

    private void OnLayout(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(QuickSettingsLayout.Pinned))
            Show();
    }

    private void OnMove(object? sender, SettingsOrderMove move)
    {
        var next = SettingsTiles.Move(_keys, move.Key, move.Offset);
        if (next is null)
            return;
        Write(next);
        Tiles.Focus(move.Key, move.Button);
    }

    private void OnRemove(object? sender, string key)
    {
        var shown = SettingsTiles.Shown(_keys);
        var index = 0;
        for (var i = 0; i < shown.Count; i++)
        {
            if (shown[i].Key == key)
                index = i;
        }
        Write(SettingsTiles.Remove(_keys, key));
        var next = SettingsOrder.FocusIndex(Ids(SettingsTiles.Shown(_keys)), index);
        if (next is not null)
            Tiles.Focus(next, "remove");
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
            return;
        var before = Ids(SettingsTiles.Unpinned(_keys));
        var index = 0;
        for (var i = 0; i < before.Count; i++)
        {
            if (before[i] == key)
                index = i;
        }
        Write(SettingsTiles.Add(_keys, key));
        var stay = SettingsOrder.FocusIndex(Ids(SettingsTiles.Unpinned(_keys)), index);
        if (stay is not null && _add.TryGetValue(stay, out var button))
        {
            button.Focus(FocusState.Programmatic);
            button.StartBringIntoView();
        }
        else
            Tiles.Focus(key, "remove");
    }

    private void Write(IReadOnlyList<string> keys) => _layout.Pinned = [.. keys.Cast<object?>()];

    private void Show()
    {
        _keys = SettingsTiles.Keys(_layout.Pinned);
        var items = new List<SettingsOrderItem>();
        foreach (var kind in SettingsTiles.Shown(_keys))
            items.Add(new SettingsOrderItem(kind.Key, kind.Title, kind.Glyph));
        Tiles.Show(items);
        var unpinned = SettingsTiles.Unpinned(_keys);
        AddHeader.Visibility = unpinned.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Add.Children.Clear();
        _add.Clear();
        foreach (var kind in unpinned)
        {
            var button = new Button { Content = "Add", Tag = kind.Key };
            AutomationProperties.SetName(button, "Add " + kind.Title);
            button.Click += OnAdd;
            var card = new SettingsCard { Title = kind.Title, Glyph = kind.Glyph, Description = "", TrailContent = button };
            _add[kind.Key] = button;
            Add.Children.Add(card);
        }
    }

    private static List<string> Ids(IReadOnlyList<QuickSettingsKind> kinds)
    {
        var ids = new List<string>(kinds.Count);
        foreach (var kind in kinds)
            ids.Add(kind.Key);
        return ids;
    }
}
