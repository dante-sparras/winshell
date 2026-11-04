using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Winshell.DefaultShell;

/// <summary>
/// The close button on a preview. At rest it is only the glyph. Under the pointer it is Windows' caption red with a
/// white glyph, in light and dark alike; a contrast theme highlights instead. The fills are theme resources, so a
/// theme change does not keep a brush from the theme that was current when this was built.
/// </summary>
public sealed partial class PreviewCloseButton : Button
{
    private bool _over;
    private bool _down;

    public PreviewCloseButton()
    {
        InitializeComponent();
        PointerEntered += (_, _) =>
        {
            _over = true;
            Show();
        };
        PointerExited += (_, _) =>
        {
            _over = false;
            _down = false;
            Show();
        };
        // Button marks a left press and release handled, so these take handled events too.
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) =>
        {
            _down = true;
            Show();
        }), handledEventsToo: true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) =>
        {
            _down = false;
            Show();
        }), handledEventsToo: true);
        PointerCanceled += (_, _) =>
        {
            _down = false;
            _over = false;
            Show();
        };
    }

    private void Show()
    {
        var pressed = _down && _over;
        var hover = _over && !pressed;
        HoverFill.Visibility = hover ? Visibility.Visible : Visibility.Collapsed;
        PressedFill.Visibility = pressed ? Visibility.Visible : Visibility.Collapsed;
        RestGlyph.Visibility = _over ? Visibility.Collapsed : Visibility.Visible;
        HoverGlyph.Visibility = hover ? Visibility.Visible : Visibility.Collapsed;
        PressedGlyph.Visibility = pressed ? Visibility.Visible : Visibility.Collapsed;
    }
}
