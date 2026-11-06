using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// Windows' peek: DWM shows one window at full size and hides the others, until it is told to stop. This is the
/// call Windows' own taskbar uses.
/// </summary>
internal static partial class DwmLivePreview
{
    private const uint PeekOneWindow = 1;

    /// <summary>
    /// Starts the peek at <paramref name="window"/>, leaving <paramref name="above"/> in front, or ends it. False when
    /// DWM refused or this build of Windows has no such call.
    /// </summary>
    internal static bool Set(bool on, nint window, nint above)
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

    // Undocumented, and the only way to ask for the peek Windows' own taskbar uses. dwmapi exports it by ordinal alone.
    [LibraryImport("dwmapi.dll", EntryPoint = "#113")]
    private static partial int DwmpActivateLivePreview([MarshalAs(UnmanagedType.Bool)] bool on, nint window, nint above,
        uint type, nuint reserved);
}
