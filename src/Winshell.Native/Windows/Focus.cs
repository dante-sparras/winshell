namespace Winshell.Native;

/// <summary>Brings a window to the front and gives it the keyboard.</summary>
internal static class Focus
{
    /// <summary>
    /// Brings <paramref name="window"/> to the front and gives it the keyboard. Windows lets a program take the
    /// foreground only in some cases, so this joins the thread that has it and, when the window still does not, taps
    /// a key. Returns whether the window has the keyboard.
    /// </summary>
    internal static bool Take(nint window)
    {
        if (window == 0 || Win32.GetForegroundWindow() == window)
            return window != 0;
        if (Ask(window))
            return true;

        var foreground = Win32.GetForegroundWindow();
        var current = Win32.GetCurrentThreadId();
        var other = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
        if (other != 0 && other != current && Win32.AttachThreadInput(current, other, true))
            try
            {
                // Joined to the foreground thread, the z-order change is allowed along with the focus change.
                TopLevelWindows.Raise(window);
                Ask(window);
            }
            finally
            {
                Win32.AttachThreadInput(current, other, false);
            }

        if (Win32.GetForegroundWindow() == window)
            return true;
        // Joining was not enough. A key makes this process the one that last sent input, which may take the foreground.
        KeyInput.Tap();
        Ask(window);
        return Win32.GetForegroundWindow() == window;
    }

    private static bool Ask(nint window)
    {
        return Win32.SetForegroundWindow(window) && Win32.GetForegroundWindow() == window;
    }
}
