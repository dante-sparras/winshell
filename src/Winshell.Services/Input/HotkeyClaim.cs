namespace Winshell.Input;

internal enum HotkeyClaimStatus
{
    Idle,
    Active,
    Taken,
    Invalid
}

/// <summary>The result of asking Windows for a hotkey.</summary>
internal readonly record struct HotkeyClaim(HotkeyClaimStatus Status, string Error)
{
    public static HotkeyClaim Idle { get; } = new(HotkeyClaimStatus.Idle, "");

    public static HotkeyClaim Active { get; } = new(HotkeyClaimStatus.Active, "");

    public static HotkeyClaim Taken(string error)
    {
        return new HotkeyClaim(HotkeyClaimStatus.Taken, error);
    }

    public static HotkeyClaim Invalid(string error)
    {
        return new HotkeyClaim(HotkeyClaimStatus.Invalid, error);
    }
}

/// <summary>The object told when its hotkey is pressed or released. Implemented explicitly so the methods stay off the public surface.</summary>
internal interface IHotkeyListener
{
    IEventLoop Loop { get; }

    /// <param name="deliversRelease">
    /// True when the caller will call <see cref="OnReleased"/> itself. False when the listener must watch the keys.
    /// </param>
    void OnPressed(bool deliversRelease);

    void OnReleased();
}
