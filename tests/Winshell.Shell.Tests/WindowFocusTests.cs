namespace Winshell.ShellTests;

/// <summary>
/// Which windows take the keyboard, and what each kind does as a result, against a stand-in for the native window.
/// What a real window does with those answers is in the Slow tests.
/// </summary>
public class WindowFocusTests
{
    /// <summary>A native window that records what changed on it, in order. Asking for what it already has changes nothing.</summary>
    private sealed class FakeWindow : IFocusEffects
    {
        private bool? _activation;
        private bool _grabbing;

        public List<string> Effects { get; } = [];

        public void AllowActivation(bool allow)
        {
            if (_activation == allow)
                return;
            _activation = allow;
            Effects.Add(allow ? "activation allowed" : "activation refused");
        }

        public void Show(bool activate)
        {
            Effects.Add(activate ? "show activating" : "show without activating");
        }

        public void TakeForeground()
        {
            Effects.Add("foreground");
        }

        public void TakeKeyboard()
        {
            Effects.Add("keyboard");
        }

        public void GrabOutsideClicks(bool on)
        {
            if (_grabbing == on)
                return;
            _grabbing = on;
            Effects.Add(on ? "grab clicks" : "release clicks");
        }

        /// <summary>What changed since the last call.</summary>
        public string[] Drain()
        {
            var seen = Effects.ToArray();
            Effects.Clear();
            return seen;
        }
    }

    private static (WindowFocus Focus, FakeWindow Window) Of(WindowKind kind)
    {
        var window = new FakeWindow();
        return (new WindowFocus(kind, window), window);
    }

    [Fact]
    public void A_floating_window_always_takes_the_keyboard()
    {
        var (focus, window) = Of(WindowKind.Floating);

        focus.Show();
        focus.TakeKeyboard();
        focus.Release();

        Assert.Equal(["show activating", "keyboard"], window.Drain());
        Assert.True(focus.ActivatesOnClick);
        Assert.False(focus.DismissesOnEscape);
    }

    [Theory]
    [InlineData(true, "activation allowed", true)]
    [InlineData(false, "activation refused", false)]
    public void A_panel_takes_the_keyboard_only_while_focusable(bool focusable, string activation, bool takes)
    {
        var (focus, window) = Of(WindowKind.Panel);

        focus.SetPanel(focusable);
        focus.Show();
        focus.TakeKeyboard();

        // Even a focusable panel is shown without taking activation; it takes the keyboard when asked.
        Assert.Equal(
            takes ? [activation, "show without activating", "keyboard"] : [activation, "show without activating"],
            window.Drain());
        Assert.Equal(takes, focus.ActivatesOnClick);
        Assert.False(focus.DismissesOnEscape);
    }

    [Fact]
    public void A_panel_that_stops_being_focusable_refuses_the_keyboard()
    {
        var (focus, window) = Of(WindowKind.Panel);

        focus.SetPanel(true);
        focus.SetPanel(false);
        window.Drain();
        focus.TakeKeyboard();

        Assert.Empty(window.Drain());
    }

    [Fact]
    public void A_popup_that_does_not_grab_focus_shows_without_activating_and_ignores_clicks_and_keys()
    {
        var (focus, window) = Of(WindowKind.Popup);

        focus.SetPopup(false);
        focus.Show();
        focus.TakeKeyboard();

        Assert.Equal(["activation refused", "show without activating"], window.Drain());
        Assert.False(focus.ActivatesOnClick);
        Assert.False(focus.DismissesOnEscape);
    }

    [Fact]
    public void A_popup_that_grabs_focus_shows_activating_takes_the_foreground_then_grabs_clicks()
    {
        var (focus, window) = Of(WindowKind.Popup);

        focus.SetPopup(true);
        focus.Show();
        focus.TakeKeyboard();

        Assert.Equal(["activation allowed", "show activating", "foreground", "grab clicks", "keyboard"],
            window.Drain());
        Assert.True(focus.ActivatesOnClick);
        Assert.True(focus.DismissesOnEscape);
    }

    [Fact]
    public void A_popup_that_grabs_focus_stops_grabbing_clicks_when_it_hides_and_grabs_again_when_shown()
    {
        var (focus, window) = Of(WindowKind.Popup);
        focus.SetPopup(true);
        focus.Show();
        window.Drain();

        focus.Release();
        Assert.Equal(["release clicks"], window.Drain());

        focus.Show();
        Assert.Equal(["show activating", "foreground", "grab clicks"], window.Drain());
    }

    [Fact]
    public void A_popup_that_grabs_focus_grabs_clicks_only_while_shown()
    {
        var (focus, window) = Of(WindowKind.Popup);

        focus.SetPopup(true);
        Assert.Equal(["activation allowed"], window.Drain());

        focus.Show();
        window.Drain();
        focus.SetPopup(true);
        Assert.Empty(window.Drain());
    }

    [Fact]
    public void A_shown_popup_that_stops_grabbing_focus_releases_clicks_and_refuses_the_keyboard()
    {
        var (focus, window) = Of(WindowKind.Popup);
        focus.SetPopup(true);
        focus.Show();
        window.Drain();

        focus.SetPopup(false);
        focus.TakeKeyboard();

        Assert.Equal(["activation refused", "release clicks"], window.Drain());
        Assert.False(focus.ActivatesOnClick);
        Assert.False(focus.DismissesOnEscape);
    }
}
