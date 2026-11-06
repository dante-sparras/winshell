using Windows.Win32;

namespace Winshell.Native;

internal static class Cursor
{
    /// <summary>Moves the pointer to a point on the screen, in physical pixels.</summary>
    internal static bool MoveTo(int x, int y)
    {
        return PInvoke.SetCursorPos(x, y);
    }
}
