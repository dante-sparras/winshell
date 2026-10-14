namespace Winshell.Core;

/// <summary>
/// The window a panel is drawn in, as <see cref="PanelSession"/> drives it.
/// Every call arrives on the UI thread, and every callback must run there too.
/// </summary>
public interface IPanelWindow
{
    /// <summary>Runs <paramref name="work"/> on a later turn of the UI thread.</summary>
    /// <param name="work">What to run.</param>
    void Post(Action work);

    /// <summary>Runs <paramref name="work"/> once, <paramref name="delay"/> from now.</summary>
    /// <param name="delay">How long to wait.</param>
    /// <param name="work">What to run.</param>
    void After(TimeSpan delay, Action work);

    /// <summary>Brings the panel to the front and gives it activation.</summary>
    /// <param name="first">True for the first raise of an open. That one also follows the Windows theme, and the panel stays unseen until <see cref="Reveal"/>.</param>
    void Raise(bool first);

    /// <summary>Runs <paramref name="drawn"/> once the window has drawn the panel it was last given, at that panel's size.</summary>
    /// <param name="drawn">What to run. A window that hides first never runs it.</param>
    void WhenDrawn(Action drawn);

    /// <summary>Lets the panel be seen and starts its entrance.</summary>
    void Reveal();

    /// <summary>Puts the panel back just under the bar.</summary>
    void KeepUnderBar();

    /// <summary>Starts hearing presses anywhere on screen. They come back on <see cref="PanelSession.PressedOutside"/>.</summary>
    void ListenOutside();

    /// <summary>Slides the panel back under the bar and hides it, or hides it at once.</summary>
    /// <param name="finished">Runs when a slide ends. A later show that stops the slide skips it.</param>
    /// <returns>True when a slide started. False when the panel hid at once.</returns>
    bool Leave(Action finished);

    /// <summary>Gives the panel the keyboard.</summary>
    void TakeKeyboard();
}
