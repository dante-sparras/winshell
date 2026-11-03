using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell;

/// <summary>Ends native menu tracking without taking the keyboard from its app.</summary>
internal static partial class NativeMenu
{
    private const uint CancelMode = 0x001F;

    internal static void Dismiss()
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info) || info.MenuOwner == 0)
            return;
        // Target the menu owner, which need not be the foreground HWND. Finish cancellation before a button
        // forwards another tray callback, but never let a hung app hold up the shell.
        Win32.SendMessageTimeoutW(info.MenuOwner, CancelMode, 0, 0, Win32.SMTO_ABORTIFHUNG | Win32.SMTO_BLOCK, 200, out _);
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
