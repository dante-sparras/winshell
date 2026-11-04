using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell;

/// <summary>Ends the Win32 menus other apps have open, as a click outside a menu does.</summary>
internal static partial class NativeMenu
{
    private const uint CancelMode = 0x001F;

    /// <summary>
    /// Cancels every open popup menu. An app that opened its menu without the foreground, which a tray menu often is,
    /// has a menu a click elsewhere does not close, so each menu's own thread is found, not only the foreground's.
    /// </summary>
    internal static void Dismiss()
    {
        foreach (var thread in MenuThreads())
        {
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            if (!GetGUIThreadInfo(thread, ref info) || info.MenuOwner == 0)
                continue;
            // Finish cancellation before a button forwards another tray callback, but never let a hung app hold up
            // the shell.
            Win32.SendMessageTimeoutW(info.MenuOwner, CancelMode, 0, 0, Win32.SMTO_ABORTIFHUNG | Win32.SMTO_BLOCK, 200, out _);
        }
    }

    /// <summary>The threads with a popup menu showing. A thread's submenus are one entry.</summary>
    private static HashSet<uint> MenuThreads()
    {
        // Windows keeps a hidden menu window ready on every desktop, so only a visible one is open.
        var threads = new HashSet<uint>();
        nint menu = 0;
        while ((menu = Win32.FindWindowExW(0, menu, "#32768", null)) != 0)
        {
            if (Win32.IsWindowVisible(menu))
                threads.Add(Win32.GetWindowThreadProcessId(menu, out _));
        }
        return threads;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
}
