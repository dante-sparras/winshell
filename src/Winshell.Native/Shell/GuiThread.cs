using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winshell.Native;

/// <summary>What Windows says about a thread's windows.</summary>
internal static unsafe class GuiThread
{
    /// <summary>The window that owns the thread's open popup menu, or 0 when it has none or the thread is gone.</summary>
    internal static nint MenuOwner(uint thread)
    {
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        return PInvoke.GetGUIThreadInfo(thread, ref info) ? (nint)info.hwndMenuOwner : 0;
    }
}
