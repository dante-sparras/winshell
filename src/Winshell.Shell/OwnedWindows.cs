using Winshell.Native;

namespace Winshell;

/// <summary>Keeps an owned window just above its owner in the z-order, and leaves every other window where it is.</summary>
internal static class OwnedWindows
{
    /// <summary>Makes <paramref name="owner"/> own <paramref name="window"/>, or nothing when it is gone or already owned by the window.</summary>
    public static void Link(nint window, HostWindow? owner)
    {
        var hwnd = owner is { Handle: not 0 } && Win32.IsWindow(owner.Handle) ? owner.Handle : 0;
        if (hwnd == window || (hwnd != 0 && OwnerChainContains(hwnd, window)))
            hwnd = 0;
        if (Win32.GetWindowLongPtr(window, Win32.GWLP_HWNDPARENT) == hwnd)
            return;
        Win32.SetWindowLongPtr(window, Win32.GWLP_HWNDPARENT, hwnd);
        if (hwnd != 0)
            PlaceAboveOwner(window);
    }

    /// <summary>Puts the windows <paramref name="owner"/> owns back above it, as after showing or restoring it.</summary>
    public static void Raise(nint owner, IEnumerable<HostWindow> windows)
    {
        foreach (var window in windows)
        {
            if (window.Handle != 0 && Win32.GetWindowLongPtr(window.Handle, Win32.GWLP_HWNDPARENT) == owner)
                PlaceAboveOwner(window.Handle);
        }
    }

    private static void PlaceAboveOwner(nint window)
    {
        var owner = Win32.GetWindowLongPtr(window, Win32.GWLP_HWNDPARENT);
        if (owner == 0 || owner == window || !Win32.IsWindow(owner))
            return;
        // Already directly above the owner.
        var above = Win32.GetWindow(owner, Win32.GW_HWNDPREV);
        if (above == window)
            return;
        // InsertAfter is the window above this one. Nothing above the owner means the top of the z-order.
        Win32.SetWindowPos(window, above == 0 ? Win32.HWND_TOP : above, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    private static bool OwnerChainContains(nint owner, nint window)
    {
        for (var hop = 0; owner != 0 && hop < 16; hop++)
        {
            if (owner == window)
                return true;
            var next = Win32.GetWindowLongPtr(owner, Win32.GWLP_HWNDPARENT);
            if (next == owner)
                return false;
            owner = next;
        }
        return false;
    }
}
