using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// The right side of one taskbar, left of the clock: hidden icons, the icons on the bar, a ready update, and quick
/// settings. A click on quick settings or the update raises an event. Nothing opens from them.
/// </summary>
public sealed partial class TrayAreaView : UserControl
{
    public TrayAreaView(TrayArea area)
    {
        Area = area;
        InitializeComponent();
    }

    public TrayArea Area { get; }

    /// <summary>The window is about to close, and the tray stays for the other bars.</summary>
    public void Release()
    {
        HiddenIcons.Hide();
        Bindings.StopTracking();
    }

    private void OnHiddenOpened(object? sender, object args) => ChevronIcon.Glyph = TrayArea.ChevronGlyph(true);

    private void OnHiddenClosed(object? sender, object args) => ChevronIcon.Glyph = TrayArea.ChevronGlyph(false);

    private Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GlyphShown(string glyph) => string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
}
