using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// Closes XAML flyouts on a click outside them. WinUI closes a flyout when its window loses activation, and a panel
/// that never activates has none to lose. A flyout shows in a window of its own, so a WinEvent hook on this process
/// notices it, and only then does a mouse hook run, until no flyout is open.
/// </summary>
internal sealed unsafe class FlyoutDismissal : IDisposable
{
    private const uint EventObjectShow = 0x8002;
    private const uint WineventOutOfContext = 0;
    private const string PopupClass = "Microsoft.UI.Content.PopupWindowSiteBridge";

    private static FlyoutDismissal? _current;

    private readonly ShellPlatform _platform;
    private readonly nint _watch;
    private HookThread? _mouse;

    public FlyoutDismissal(ShellPlatform platform)
    {
        _platform = platform;
        _current = this;
        _watch = SetWinEventHook(EventObjectShow, EventObjectShow, 0, &OnWinEvent, (uint)Environment.ProcessId, 0, WineventOutOfContext);
    }

    /// <summary>The mouse hook is running, so a flyout is open.</summary>
    internal bool Watching => _mouse is not null;

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || _current is not { } self || !IsPopupWindow(hwnd))
            return;
        NativeCallback.Guard(self._platform.Log, "shell", () => self._platform.Loop.Post(self.StartWatching));
    }

    private static bool IsPopupWindow(nint hwnd)
    {
        Span<char> name = stackalloc char[64];
        fixed (char* chars = name)
        {
            var length = GetClassNameW(hwnd, chars, name.Length);
            return length > 0 && name[..length].SequenceEqual(PopupClass);
        }
    }

    internal void StartWatching() => _mouse ??= HookThread.Start(Win32.WH_MOUSE_LL, &MouseHook, "Winshell flyout mouse hook");

    private void StopWatching()
    {
        _mouse?.Stop();
        _mouse = null;
    }

    // The procedure runs on the hook thread, so it only reads the click and posts the rest to the loop.
    [UnmanagedCallersOnly]
    private static nint MouseHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _current is { } self
            && (uint)wParam is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or Win32.WM_XBUTTONDOWN)
        {
            var pt = ((MSLLHOOKSTRUCT*)lParam)->pt;
            NativeCallback.Guard(self._platform.Log, "shell", () => self._platform.Loop.Post(() => self.Click(pt)));
        }
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>Closes every open flyout the click landed outside of, and stops watching once none is left.</summary>
    internal void Click(POINT pt)
    {
        var open = false;
        foreach (var window in _platform.Windows)
        {
            if (window.XamlRoot is not { } root)
                continue;
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            {
                if (popup.Child is FrameworkElement child && Contains(child, root, pt, window))
                    open = true;
                else
                    popup.IsOpen = false;
            }
        }
        if (!open)
            StopWatching();
    }

    private static bool Contains(FrameworkElement child, XamlRoot root, POINT pt, HostWindow window)
    {
        var scale = root.RasterizationScale;
        var corner = child.TransformToVisual(null).TransformPoint(default);
        var (left, top) = window.MapToScreen(corner.X, corner.Y);
        return pt.X >= left && pt.Y >= top && pt.X < left + child.ActualWidth * scale && pt.Y < top + child.ActualHeight * scale;
    }

    public void Dispose()
    {
        if (_watch != 0)
            UnhookWinEvent(_watch);
        StopWatching();
        _current = null;
    }

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback,
        uint process,
        uint thread,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern int GetClassNameW(nint hwnd, char* name, int length);
}
