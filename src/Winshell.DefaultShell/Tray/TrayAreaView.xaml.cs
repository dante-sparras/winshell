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
    private bool _hiddenOpen;
    private bool _menuOpen;

    public TrayAreaView(TrayArea area)
    {
        Area = area;
        InitializeComponent();
    }

    public TrayArea Area { get; }

    /// <summary>The button the flyout sits above.</summary>
    public Button QuickSettingsAnchor => QuickSettingsButton;

    /// <summary>The hidden icons or Winland's menu are open, so an auto-hiding taskbar stays up.</summary>
    internal bool Open => _hiddenOpen || _menuOpen;

    /// <summary><see cref="Open"/> changed.</summary>
    internal event EventHandler? OpenChanged;

    /// <summary>The window is about to close, and the tray stays for the other bars.</summary>
    public void Release()
    {
        HiddenIcons.Hide();
        Bindings.StopTracking();
    }

    /// <summary>An icon's own menu opened or closed.</summary>
    internal void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnIconPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is TrayIconButton button)
            button.Tray = this;
    }

    private void OnHiddenOpened(object? sender, object args)
    {
        ChevronIcon.Glyph = TrayArea.ChevronGlyph(true);
        _hiddenOpen = true;
        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHiddenClosed(object? sender, object args)
    {
        ChevronIcon.Glyph = TrayArea.ChevronGlyph(false);
        _hiddenOpen = false;
        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    private Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GlyphShown(string glyph) => string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
}
