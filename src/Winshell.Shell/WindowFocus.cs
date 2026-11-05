using Winshell.Native;

namespace Winshell;

/// <summary>What <see cref="WindowFocus"/> does to the native window, and to the clicks around it.</summary>
internal interface IFocusEffects
{
    /// <summary>Lets the window take activation from the app in front, or stops it.</summary>
    void AllowActivation(bool allow);

    /// <summary>Shows the window, taking activation from the app in front or not.</summary>
    void Show(bool activate);

    /// <summary>Puts the shown window in front.</summary>
    void TakeForeground();

    /// <summary>Puts the window in front and hands its content the keyboard.</summary>
    void TakeKeyboard();

    /// <summary>Starts or stops dismissing the window on a click outside it.</summary>
    void GrabOutsideClicks(bool on);
}

/// <summary>
/// Whether a window takes the keyboard, and with it activation from the app in front, and the order the window does
/// what follows from it. A floating window always takes it, a panel only while it is focusable, and a popup window only
/// while it grabs focus. The kind decides the styles a window starts with, how it shows, whether a click or
/// <see cref="TakeKeyboard"/> activates it, whether Esc dismisses it, and when a click outside it does.
/// </summary>
internal sealed class WindowFocus(WindowKind kind, IFocusEffects effects)
{
    private bool _focusable;
    private bool _grabFocus;
    private bool _shown;

    /// <summary>The extended style a native window starts with: a panel and a popup window start out refusing activation.</summary>
    internal static uint StartingExStyle(WindowKind kind) => kind == WindowKind.Floating ? 0 : Win32.WS_EX_NOACTIVATE;

    /// <summary>Whether a click on the window activates it, and with it the keyboard.</summary>
    internal bool ActivatesOnClick => Takes;

    /// <summary>Whether Esc dismisses the window.</summary>
    internal bool DismissesOnEscape => kind == WindowKind.Popup && _grabFocus;

    private bool Takes => kind switch
    {
        WindowKind.Floating => true,
        WindowKind.Panel => _focusable,
        _ => _grabFocus,
    };

    /// <summary>A panel's focusable setting changed.</summary>
    internal void SetPanel(bool focusable)
    {
        _focusable = focusable;
        effects.AllowActivation(Takes);
    }

    /// <summary>A popup window's grab setting changed.</summary>
    internal void SetPopup(bool grabFocus)
    {
        _grabFocus = grabFocus;
        effects.AllowActivation(Takes);
        effects.GrabOutsideClicks(_shown && grabFocus);
    }

    /// <summary>Shows the window. A popup window that grabs focus then takes the foreground and starts grabbing clicks.</summary>
    internal void Show()
    {
        _shown = true;
        var grabbing = kind == WindowKind.Popup && _grabFocus;
        effects.Show(activate: kind == WindowKind.Floating || grabbing);
        if (grabbing)
            effects.TakeForeground();
        if (kind == WindowKind.Popup)
            effects.GrabOutsideClicks(grabbing);
    }

    /// <summary>The window is hidden, or going away: a popup window stops grabbing clicks.</summary>
    internal void Release()
    {
        _shown = false;
        if (kind == WindowKind.Popup)
            effects.GrabOutsideClicks(false);
    }

    /// <summary>Takes the keyboard, if this kind of window may.</summary>
    internal void TakeKeyboard()
    {
        if (Takes)
            effects.TakeKeyboard();
    }
}
