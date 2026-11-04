using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// Quick settings for one taskbar: the tiles, the sliders and the pages, above a footer. Escape leaves a page, and
/// then the window closes the flyout.
/// </summary>
public sealed partial class QuickSettingsView : UserControl
{
    public QuickSettingsView(QuickSettingsPopup popup)
    {
        Popup = popup;
        InitializeComponent();
        // Main is collapsed while a page shows, so Pages keeps the height it had.
        Main.SizeChanged += (_, e) =>
        {
            if (Popup.ShowingTiles)
                Pages.Height = e.NewSize.Height;
        };
        Popup.PropertyChanged += OnPopup;
        VolumeRow.Moved += (_, value) => Popup.Settings.Sound.SetVolume(value);
        VolumeRow.GlyphActivated += (_, _) => Popup.Settings.Sound.ToggleMute();
        VolumeRow.PageActivated += (_, _) => Popup.ShowOutput();
        BrightnessRow.Moved += (_, value) => Popup.Settings.Brightness.SetLevel(value);
    }

    public QuickSettingsPopup Popup { get; }

    /// <summary>The tile grid, so a drag can say where the pointer is among the tiles.</summary>
    internal ItemsControl TileHost => Tiles;

    /// <summary>How tall the content is, whatever height the window has now.</summary>
    public double ContentHeight => Body.ActualHeight;

    /// <summary>The content changed height, as it does between pages.</summary>
    public event SizeChangedEventHandler ContentSized
    {
        add => Body.SizeChanged += value;
        remove => Body.SizeChanged -= value;
    }

    /// <summary>Drops the bindings. The window is about to close.</summary>
    public void Release()
    {
        Popup.PropertyChanged -= OnPopup;
        Bindings.StopTracking();
    }

    private void OnPopup(object? sender, PropertyChangedEventArgs e)
    {
        // Once the bindings have shown the page, so that its Back button can take focus.
        if (e.PropertyName == nameof(QuickSettingsPopup.Page) && !Popup.ShowingTiles)
            DispatcherQueue.TryEnqueue(() => (FocusManager.FindFirstFocusableElement(Pages) as Control)?.Focus(FocusState.Programmatic));
    }

    private Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private Visibility BatteryShown(bool hasBattery, bool browsing) => Shown(hasBattery && browsing);

    private bool CanAdd(bool hasUnpinned, bool showingAdd) => hasUnpinned && !showingAdd;

    private ImageSource? MediaImage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? new BitmapImage(uri) : null;

    private void OnTileGridLoaded(object sender, RoutedEventArgs e) =>
        ((VariableSizedWrapGrid)sender).MaximumRowsOrColumns = QuickSettings.TileColumns;

    private void OnAddTile(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
            Popup.Add(key);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && Popup.Escape())
            e.Handled = true;
    }
}
