using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// A 96 by 48 tile with its name under it. A toggle tile uses the accent fill while it is on. While quick settings is
/// being edited, the tile has an unpin badge, can be dragged, and moves with Alt+Shift and the arrow keys.
/// </summary>
public sealed partial class QuickSettingsTileView : UserControl
{
    public static readonly DependencyProperty TileProperty = DependencyProperty.Register(
        nameof(Tile), typeof(QuickSettingsTile), typeof(QuickSettingsTileView), new PropertyMetadata(null, OnTileChanged));

    private QuickSettingsTile? _tile;
    private QuickSettingsPopup? _popup;
    private bool _dragging;

    public QuickSettingsTileView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public QuickSettingsTile? Tile
    {
        get => (QuickSettingsTile?)GetValue(TileProperty);
        set => SetValue(TileProperty, value);
    }

    private static void OnTileChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((QuickSettingsTileView)sender).Follow((QuickSettingsTile?)e.NewValue);

    private void Attach()
    {
        var popup = FindPopup();
        if (!ReferenceEquals(popup, _popup))
        {
            if (_popup is not null)
                _popup.PropertyChanged -= OnPopup;
            _popup = popup;
            if (_popup is not null)
                _popup.PropertyChanged += OnPopup;
        }
        ShowEdit();
        FocusIfAsked();
    }

    private void Detach()
    {
        if (_popup is not null)
            _popup.PropertyChanged -= OnPopup;
        _popup = null;
        _dragging = false;
        Follow(null);
    }

    private void Follow(QuickSettingsTile? tile)
    {
        if (_tile is not null)
            _tile.PropertyChanged -= OnTile;
        _tile = tile;
        if (_tile is not null)
        {
            _tile.PropertyChanged += OnTile;
            AutomationProperties.SetName(Unpin, "Unpin " + _tile.Title);
        }
        ShowOn();
        ShowEdit();
    }

    private void OnTile(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSettingsTile.On))
            ShowOn();
        else if (e.PropertyName == nameof(QuickSettingsTile.Description))
            ShowEdit();
    }

    private void OnPopup(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QuickSettingsPopup.Editing) or nameof(QuickSettingsPopup.DragIndex) or nameof(QuickSettingsPopup.DropIndex))
            ShowEdit();
        else if (e.PropertyName == nameof(QuickSettingsPopup.FocusKey))
            FocusIfAsked();
    }

    private void ShowOn() => Toggle.IsChecked = _tile?.On == true;

    private void ShowEdit()
    {
        var editing = _popup?.Editing == true;
        var index = _tile is null || _popup is null ? -1 : _popup.Settings.Pins.Shown.IndexOf(_tile);
        var side = index < 0 || _popup is null ? 0 : _popup.DropSide(index);
        var help = editing
            ? "Alt+Shift+arrow keys move it, Delete unpins it"
            : _tile?.Description ?? "";
        AutomationProperties.SetHelpText(Toggle, help);
        AutomationProperties.SetHelpText(Plain, help);
        var toggle = _tile?.Toggle == true && !editing;
        Toggle.Visibility = Shown(toggle);
        Plain.Visibility = Shown(_tile is not null && !toggle);
        Drag.Visibility = Shown(editing);
        Unpin.Visibility = Shown(editing);
        Opacity = index < 0 || _popup is null ? 1 : _popup.DragOpacity(index);
        DropMark.Visibility = Shown(side != 0);
        if (side != 0)
            DropMark.Margin = new Thickness(side < 0 ? -7 : 101, 0, 0, 0);
    }

    private void FocusIfAsked()
    {
        if (_tile is null || _popup is null || _popup.FocusKey != _tile.Key)
            return;
        var button = Toggle.Visibility == Visibility.Visible ? (Control)Toggle : Plain;
        button.Focus(FocusState.Programmatic);
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton)
            Toggle.IsChecked = _tile?.On == true;
        if (_tile is not { } tile || _popup?.Editing == true)
            return;
        // WinUI does not expose ButtonBase.ClickEvent, so the click cannot be caught above the tile.
        FindView()?.Popup.Activate(tile);
    }

    private void OnUnpin(object sender, RoutedEventArgs e)
    {
        if (_tile is { } tile)
            _popup?.Unpin(tile.Key, fromKeyboard: false);
    }

    /// <summary>How far Alt+Shift and an arrow move a tile. Any other key, or without both modifiers, stays put.</summary>
    internal static int KeyOffset(bool alt, bool shift, VirtualKey key)
    {
        if (!alt || !shift)
            return 0;
        return key switch
        {
            VirtualKey.Left => -1,
            VirtualKey.Right => 1,
            VirtualKey.Up => -QuickSettings.TileColumns,
            VirtualKey.Down => QuickSettings.TileColumns,
            _ => 0,
        };
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_popup is not { Editing: true } || _tile is null)
            return;
        var offset = KeyOffset(Down(VirtualKey.Menu), Down(VirtualKey.Shift), e.Key);
        if (offset != 0)
        {
            _popup.MoveBy(_tile.Key, offset);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Delete)
        {
            _popup.Unpin(_tile.Key, fromKeyboard: true);
            e.Handled = true;
        }
    }

    private void OnDragPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_popup is null || _tile is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _dragging = true;
        Drag.CapturePointer(e.Pointer);
        _popup.BeginDrag(_tile);
        e.Handled = true;
    }

    private void OnDragMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _popup is null || FindView()?.TileHost is not { } tiles)
            return;
        var point = e.GetCurrentPoint(tiles).Position;
        _popup.DragTo(point.X, point.Y, ActualHeight);
    }

    private void OnDragReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        Drag.ReleasePointerCaptures();
        var popup = _popup;
        if (popup is null)
            return;
        // The tiles are built again for the new order, so not while this one handles the release.
        DispatcherQueue.TryEnqueue(popup.EndDrag);
        e.Handled = true;
    }

    private static bool Down(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private QuickSettingsPopup? FindPopup() => FindView()?.Popup;

    private QuickSettingsView? FindView()
    {
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is QuickSettingsView view)
                return view;
        }
        return null;
    }

    private Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
