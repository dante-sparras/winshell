using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.DefaultShell;

/// <summary>Rows in an order the user picks. After a move or a remove, the keyboard stays on that button.</summary>
public sealed partial class SettingsOrderList : UserControl
{
    private IReadOnlyList<SettingsOrderItem> _items = [];
    private readonly List<SettingsOrderRow> _rows = [];

    public SettingsOrderList() => InitializeComponent();

    public string RemoveLabel { get; set; } = "Remove";

    public string RemoveGlyph { get; set; } = "\uE738";

    public string EmptyText { get; set; } = "";

    public event EventHandler<SettingsOrderMove>? MoveRequested;

    public event EventHandler<string>? RemoveRequested;

    public void Show(IReadOnlyList<SettingsOrderItem> items)
    {
        if (Same(items))
            return;
        _items = items;
        Rows.Children.Clear();
        _rows.Clear();
        Empty.Text = EmptyText;
        AutomationProperties.SetName(Empty, EmptyText);
        Empty.Visibility = items.Count == 0 && EmptyText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < items.Count; i++)
        {
            var row = new SettingsOrderRow();
            row.Bind(items[i], i, items.Count, RemoveLabel, RemoveGlyph);
            row.Move += OnMove;
            row.Removed += OnRemoved;
            _rows.Add(row);
            Rows.Children.Add(row);
        }
    }

    public void Focus(string key, string button)
    {
        SettingsOrderRow? row = null;
        foreach (var item in _rows)
        {
            if (item.Key == key)
            {
                row = item;
                break;
            }
        }
        if (row is null)
            return;
        var target = button == "up" ? row.UpButton : button == "down" ? row.DownButton : row.RemoveButton;
        if (!target.IsEnabled)
            target = row.RemoveButton;
        target.Focus(FocusState.Programmatic);
        target.StartBringIntoView();
    }

    private bool Same(IReadOnlyList<SettingsOrderItem> items)
    {
        if (items.Count != _items.Count)
            return false;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] != _items[i])
                return false;
        }
        return true;
    }

    private void OnMove(object? sender, int offset)
    {
        if (sender is not SettingsOrderRow row)
            return;
        var button = offset < 0 ? "up" : "down";
        MoveRequested?.Invoke(this, new SettingsOrderMove(row.Key, offset, button));
    }

    private void OnRemoved(object? sender, EventArgs e)
    {
        if (sender is not SettingsOrderRow row)
            return;
        RemoveRequested?.Invoke(this, row.Key);
    }
}
