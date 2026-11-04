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
    private List<string> _keys = [];

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
        if (QuickSettingsOrder.Step(Ids(Shown()), move.Key, move.Offset) is not { } target || QuickSettingsOrder.Move(_keys, move.Key, target) is not { } next)
            return;
        Write(next);
        Tiles.Focus(move.Key, move.Button);
    }

    private void OnRemove(object? sender, string key)
    {
        var index = Math.Max(0, Ids(Shown()).IndexOf(key));
        Write(QuickSettingsOrder.Remove(_keys, key));
        var next = SettingsOrder.FocusIndex(Ids(Shown()), index);
        if (next is not null)
            Tiles.Focus(next, "remove");
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
            return;
        var index = Math.Max(0, Ids(Unpinned()).IndexOf(key));
        Write(QuickSettingsOrder.Add(_keys, key));
        var stay = SettingsOrder.FocusIndex(Ids(Unpinned()), index);
        if (stay is not null && _add.TryGetValue(stay, out var button))
        {
            button.Focus(FocusState.Programmatic);
            button.StartBringIntoView();
        }
        else
            Tiles.Focus(key, "remove");
    }

    private void Write(IReadOnlyList<string> keys) => QuickSettingsOrder.Save(_layout, keys);

    private List<QuickSettingsKind> Shown() =>
        [.. _keys.Select(key => QuickSettingsKind.All.FirstOrDefault(kind => kind.Key == key)).OfType<QuickSettingsKind>()];

    private List<QuickSettingsKind> Unpinned() => [.. QuickSettingsKind.All.Where(kind => !_keys.Contains(kind.Key))];

    private void Show()
    {
        _keys = QuickSettingsOrder.Keys(_layout.Pinned);
        var items = new List<SettingsOrderItem>();
        foreach (var kind in Shown())
            items.Add(new SettingsOrderItem(kind.Key, kind.Title, kind.Glyph));
        Tiles.Show(items);
        var unpinned = Unpinned();
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

    private static List<string> Ids(IReadOnlyList<QuickSettingsKind> kinds) => [.. kinds.Select(kind => kind.Key)];
}
