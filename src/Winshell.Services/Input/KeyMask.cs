using Winshell.Native;

namespace Winshell.Input;

/// <summary>
/// One down and up of an unassigned virtual key. Sent while the Windows key is still down, it marks that press as
/// used so Start does not open, without leaving the Windows key logically held. The hook ignores this injection.
/// </summary>
internal static class KeyMask
{
    /// <summary>Unassigned virtual key. The same value AutoHotkey uses for its menu mask.</summary>
    public const ushort VirtualKey = KeyInput.Unassigned;

    /// <summary>Tag on <c>dwExtraInfo</c> so the low-level hook does not treat the mask as a user key.</summary>
    public const nint ExtraInfo = KeyInput.Marker;

    public static void Tap()
    {
        KeyInput.Tap();
    }

    internal static bool IsOurs(nint extraInfo)
    {
        return extraInfo == ExtraInfo;
    }

    internal static KeyStroke[] Stroke()
    {
        return KeyInput.Stroke();
    }

    internal static int InputSize => KeyInput.Size;
}
