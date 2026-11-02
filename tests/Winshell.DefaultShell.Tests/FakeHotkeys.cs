using Winshell.Input;

namespace Winshell.DefaultShell.Tests;

/// <summary>Claims hotkeys without registering them with Windows. A sequence in <see cref="Taken"/> is refused, as Explorer does.</summary>
internal sealed class FakeHotkeys : IHotkeyRegistrar
{
    public HashSet<string> Taken { get; } = new(StringComparer.Ordinal);

    public List<(IHotkeyListener Listener, string Text)> Claims { get; } = [];

    public HotkeyClaim Claim(IHotkeyListener listener, HotkeySequence? sequence)
    {
        Release(listener);
        if (sequence is null || sequence.Chords.Count == 0)
            return HotkeyClaim.Idle;
        if (Taken.Contains(sequence.Text))
            return HotkeyClaim.Taken($"{sequence.Text} is already in use by Windows or another app.");
        Claims.Add((listener, sequence.Text));
        return HotkeyClaim.Active;
    }

    public void Release(IHotkeyListener listener) => Claims.RemoveAll(claim => claim.Listener == listener);

    public void Press(string sequence)
    {
        var claim = Claims.Single(item => item.Text == sequence);
        claim.Listener.OnPressed(true);
        claim.Listener.OnReleased();
    }
}
