using Microsoft.Win32;
using static Winshell.Windows.NativeMethods;

namespace Winshell.Windows;

/// <summary>
/// Windows' peek: DWM shows one window at full size and hides the others, until it is told to stop. It stays off
/// when the user turned peek off in System Properties, and when this build of Windows has no such call.
/// </summary>
internal sealed class WindowPeek(Func<bool> turnedOn, Func<bool, nint, nint, bool> set)
{
    private const uint PeekOneWindow = 1;

    private nint _window;
    private nint _above;

    public static WindowPeek ForSystem() => new(SettingIsOn, SetLivePreview);

    /// <summary>Peeks at a window, or at nothing when it is 0, leaving the window <paramref name="above"/> in front.</summary>
    public void Show(nint window, nint above)
    {
        if (window == _window && above == _above)
            return;
        End();
        if (window != 0 && turnedOn() && set(true, window, above))
            (_window, _above) = (window, above);
    }

    public void End()
    {
        if (_window == 0)
            return;
        set(false, _window, _above);
        _window = 0;
        _above = 0;
    }

    private static bool SettingIsOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        return key?.GetValue("EnableAeroPeek") is int and not 0;
    }

    private static bool SetLivePreview(bool on, nint window, nint above)
    {
        try
        {
            return DwmpActivateLivePreview(on, window, above, PeekOneWindow, 0) == 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
