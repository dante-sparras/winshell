using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        Main.SizeChanged += (_, e) => Pages.Height = e.NewSize.Height;
        VolumeRow.Moved += (_, value) => Popup.Settings.SetVolume(value);
        VolumeRow.GlyphActivated += (_, _) => Popup.Settings.ToggleMute();
        VolumeRow.PageActivated += (_, _) => Popup.ShowOutput();
        BrightnessRow.Moved += (_, value) => Popup.Settings.SetBrightness(value);
    }

    public QuickSettingsPopup Popup { get; }

    /// <summary>How tall the content is, whatever height the window has now.</summary>
    public double ContentHeight => Body.ActualHeight;

    /// <summary>The content changed height, as it does between pages.</summary>
    public event SizeChangedEventHandler ContentSized
    {
        add => Body.SizeChanged += value;
        remove => Body.SizeChanged -= value;
    }

    /// <summary>Drops the bindings. The window is about to close.</summary>
    public void Release() => Bindings.StopTracking();

    private double Fade(bool shown) => shown ? 1 : 0;

    private Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && Popup.Escape())
            e.Handled = true;
    }
}
