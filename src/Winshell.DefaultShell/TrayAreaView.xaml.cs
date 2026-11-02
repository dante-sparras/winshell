using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// The right side of one taskbar, left of the clock: hidden icons, the icons on the bar, a ready update, and quick
/// settings. A click on quick settings raises an event here and on the shared area. This bar opens the flyout.
/// </summary>
public sealed partial class TrayAreaView : UserControl
{
    public TrayAreaView(TrayArea area)
    {
        Area = area;
        InitializeComponent();
    }

    public TrayArea Area { get; }

    /// <summary>The quick settings button on this bar was clicked.</summary>
    public event EventHandler? QuickSettingsClicked;

    /// <summary>The button the flyout sits above.</summary>
    public FrameworkElement QuickSettingsAnchor => QuickSettingsButton;

    /// <summary>The window is about to close, and the tray stays for the other bars.</summary>
    public void Release()
    {
        HiddenIcons.Hide();
        Bindings.StopTracking();
    }

    private void OnQuickSettings(object sender, RoutedEventArgs e)
    {
        QuickSettingsClicked?.Invoke(this, EventArgs.Empty);
        Area.RequestQuickSettings();
    }

    private void OnHiddenOpened(object? sender, object args) => ChevronIcon.Glyph = TrayArea.ChevronGlyph(true);

    private void OnHiddenClosed(object? sender, object args) => ChevronIcon.Glyph = TrayArea.ChevronGlyph(false);

    private Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GlyphShown(string glyph) => string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
}
