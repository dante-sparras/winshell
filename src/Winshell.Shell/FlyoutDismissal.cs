using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// Closes XAML flyouts on a click outside them. WinUI closes a flyout when its window loses activation, and a panel
/// that never activates has none to lose. A flyout shows in a window of its own, so a WinEvent hook notices this
/// process's, and only then does a mouse hook run, until no flyout is open.
/// </summary>
internal sealed class FlyoutDismissal : IDisposable
{
    private const string PopupClass = "Microsoft.UI.Content.PopupWindowSiteBridge";

    private readonly ShellPlatform _platform;
    private readonly WinEventHook? _watch;
    private IDisposable? _mouse;

    public FlyoutDismissal(ShellPlatform platform)
    {
        _platform = platform;
        _watch = WinEventHook.Start(Win32.EVENT_OBJECT_SHOW, OnWinEvent);
    }

    /// <summary>The mouse hook is running, so a flyout is open.</summary>
    internal bool Watching => _mouse is not null;

    private void OnWinEvent(uint evt, nint hwnd, int idObject, int idChild)
    {
        if (idObject != 0 || !IsPopupWindow(hwnd))
            return;
        NativeCallback.Guard(_platform.Log, "shell", () => _platform.Loop.Post(StartWatching));
    }

    /// <summary>A flyout's window in this process. The hook sees every process's windows.</summary>
    private static bool IsPopupWindow(nint hwnd) =>
        Win32.GetWindowThreadProcessId(hwnd, out var process) != 0 && process == Environment.ProcessId
        && TopLevelWindows.ClassName(hwnd) == PopupClass;

    internal void StartWatching() => _mouse ??= LowLevelMouse.Start(OnPress);

    private void StopWatching()
    {
        _mouse?.Dispose();
        _mouse = null;
    }

    // This runs on the hook thread, so it only posts the click to the loop.
    private void OnPress(POINT pt) => NativeCallback.Guard(_platform.Log, "shell", () => _platform.Loop.Post(() => Click(pt)));

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
        _watch?.Dispose();
        StopWatching();
    }
}
