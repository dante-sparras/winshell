using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Input;

internal enum HotkeyClaimStatus
{
    Idle,
    Active,
    Taken,
    Invalid,
}

/// <summary>The result of asking Windows for a hotkey.</summary>
internal readonly record struct HotkeyClaim(HotkeyClaimStatus Status, string Error)
{
    public static HotkeyClaim Idle { get; } = new(HotkeyClaimStatus.Idle, "");

    public static HotkeyClaim Active { get; } = new(HotkeyClaimStatus.Active, "");

    public static HotkeyClaim Taken(string error) => new(HotkeyClaimStatus.Taken, error);

    public static HotkeyClaim Invalid(string error) => new(HotkeyClaimStatus.Invalid, error);
}

/// <summary>The object told when its hotkey is pressed or released. Implemented explicitly so the methods stay off QML.</summary>
internal interface IHotkeyListener
{
    IEventLoop Loop { get; }

    /// <param name="deliversRelease">
    /// True when the caller will call <see cref="OnReleased"/> itself. False when the listener must watch the keys.
    /// </param>
    void OnPressed(bool deliversRelease);

    void OnReleased();
}

/// <summary>Claims and releases hotkeys for one shell process.</summary>
internal interface IHotkeyRegistrar
{
    HotkeyClaim Claim(IHotkeyListener listener, HotkeySequence? sequence);

    void Release(IHotkeyListener listener);
}
