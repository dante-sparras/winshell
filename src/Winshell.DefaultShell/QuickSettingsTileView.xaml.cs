using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>A 96 by 48 tile with its name under it. A toggle tile uses the accent fill while it is on.</summary>
public sealed partial class QuickSettingsTileView : UserControl
{
    public static readonly DependencyProperty TileProperty = DependencyProperty.Register(
        nameof(Tile), typeof(QuickSettingsTile), typeof(QuickSettingsTileView), new PropertyMetadata(null, OnTileChanged));

    private QuickSettingsTile? _tile;

    public QuickSettingsTileView()
    {
        InitializeComponent();
        Unloaded += (_, _) => Follow(null);
    }

    public QuickSettingsTile? Tile
    {
        get => (QuickSettingsTile?)GetValue(TileProperty);
        set => SetValue(TileProperty, value);
    }

    private static void OnTileChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((QuickSettingsTileView)sender).Follow((QuickSettingsTile?)e.NewValue);

    private void Follow(QuickSettingsTile? tile)
    {
        if (_tile is not null)
            _tile.PropertyChanged -= OnTile;
        _tile = tile;
        if (_tile is not null)
            _tile.PropertyChanged += OnTile;
        ShowOn();
    }

    private void OnTile(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSettingsTile.On))
            ShowOn();
    }

    private void ShowOn() => Toggle.IsChecked = _tile?.On == true;

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton)
            Toggle.IsChecked = _tile?.On == true;
        if (_tile is not { } tile)
            return;
        // WinUI does not expose ButtonBase.ClickEvent, so the click cannot be caught above the tile.
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is QuickSettingsView view)
            {
                view.Popup.Activate(tile);
                return;
            }
        }
    }

    private Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private Visibility Hidden(bool visible) => visible ? Visibility.Collapsed : Visibility.Visible;
}
