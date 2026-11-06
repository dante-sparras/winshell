using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// Handles a click outside the popups that grab focus and the XAML flyouts. Such a popup is dismissed by a click
/// outside it, and a flyout closes on one: WinUI closes a flyout when its window loses activation, and a panel that
/// never activates has none to lose. A click inside an open flyout counts as inside the window that owns it, so a menu
/// that reaches past its popup leaves the popup open. A flyout shows in a window of its own, so a WinEvent hook
/// notices when one of this process's flyout windows shows. The mouse hook runs only while a popup grabs or a flyout
/// is open.
/// </summary>
internal sealed class OutsideClicks : IDisposable
{
    private const string PopupClass = "Microsoft.UI.Content.PopupWindowSiteBridge";

    private readonly ShellPlatform _platform;
    private readonly HashSet<HostWindow> _grabbing = [];
    private readonly WinEventHook? _watch;
    private IDisposable? _mouse;
    private bool _flyouts;

    public OutsideClicks(ShellPlatform platform)
    {
        _platform = platform;
        _watch = WinEventHook.Start(Win32.EVENT_OBJECT_SHOW, OnWinEvent);
    }

    private void OnWinEvent(uint evt, nint hwnd, int idObject, int idChild)
    {
        if (idObject != 0 || !IsPopupWindow(hwnd))
            return;
        NativeCallback.Guard(_platform.Log, "shell", () => _platform.Loop.Post(() =>
        {
            _flyouts = true;
            Update();
        }));
    }

    /// <summary>A flyout's window in this process. The hook sees every process's windows.</summary>
    private static bool IsPopupWindow(nint hwnd)
    {
        return Win32.GetWindowThreadProcessId(hwnd, out var process) != 0 && process == Environment.ProcessId
                                                                          && TopLevelWindows.ClassName(hwnd) ==
                                                                          PopupClass;
    }

    /// <summary>Starts or stops dismissing a popup on a click outside it.</summary>
    internal void Grab(HostWindow window, bool on)
    {
        if (on && window.Handle != 0)
            _grabbing.Add(window);
        else
            _grabbing.Remove(window);
        Update();
    }

    private void Update()
    {
        if (_grabbing.Count > 0 || _flyouts)
        {
            _mouse ??= LowLevelMouse.Start(OnPress);
            return;
        }

        _mouse?.Dispose();
        _mouse = null;
    }

    // This runs on the hook thread, so it only posts the click to the loop.
    private void OnPress(POINT pt)
    {
        NativeCallback.Guard(_platform.Log, "shell", () => _platform.Loop.Post(() => Click(pt)));
    }

    /// <summary>
    /// Closes every open flyout the click landed outside of, then dismisses every grabbing popup it landed outside of,
    /// where a popup's open flyouts count as the popup.
    /// </summary>
    internal void Click(POINT pt)
    {
        HashSet<HostWindow> inside = [];
        _flyouts = false;
        foreach (var window in _platform.Windows)
        {
            if (window.Presentation.XamlRoot is not { } root)
                continue;
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
                if (popup.Child is FrameworkElement child && Contains(child, root, pt, window))
                {
                    _flyouts = true;
                    inside.Add(window);
                }
                else
                {
                    popup.IsOpen = false;
                }
        }

        foreach (var window in _grabbing.ToArray())
            if (!inside.Contains(window) && !window.Placement.ContainsScreenPoint(pt))
                window.Client.OnDismissed();
        Update();
    }

    private static bool Contains(FrameworkElement child, XamlRoot root, POINT pt, HostWindow window)
    {
        var scale = root.RasterizationScale;
        var corner = child.TransformToVisual(null).TransformPoint(default);
        var (left, top) = window.Placement.MapToScreen(corner.X, corner.Y);
        return pt.X >= left && pt.Y >= top && pt.X < left + child.ActualWidth * scale &&
               pt.Y < top + child.ActualHeight * scale;
    }

    public void Dispose()
    {
        _watch?.Dispose();
        _mouse?.Dispose();
        _mouse = null;
    }
}
