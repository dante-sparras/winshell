using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinRT;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// What a native window shows, and the keyboard that reaches it: a XAML island whose background stays transparent, over
/// a material, both following the shell's theme. The root exists with the window's parts; the island once the window does.
/// </summary>
internal sealed class HostPresentation(HostWindow host, ShellPlatform platform) : IDisposable
{
    private readonly Grid _root = new() { Background = null };
    private DesktopWindowXamlSource? _xaml;
    private SystemBackdropConfiguration? _backdropConfig;
    private ISystemBackdropControllerWithTargets? _backdropController;

    private nint Handle => host.Handle;

    /// <summary>What the window shows. Its background stays transparent, so the material shows through.</summary>
    public UIElement? Content
    {
        get => _root.Children.Count > 0 ? _root.Children[0] : null;
        set
        {
            _root.Children.Clear();
            if (value is not null)
                _root.Children.Add(value);
        }
    }

    public XamlRoot? XamlRoot => _root.XamlRoot;

    /// <summary>Puts the content in the native window, which exists now, and follows the theme from then on.</summary>
    public void Attach()
    {
        // The material shows wherever the content is transparent.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Win32.DwmExtendFrameIntoClientArea(Handle, margins);
        if (host.Kind != WindowKind.Floating)
        {
            // Panels and popups appear without the Windows fade, as layer surfaces do.
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_TRANSITIONS_FORCEDISABLED, 1, 4);
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_EXCLUDED_FROM_PEEK, 1, 4);
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, 1, 4);
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_BORDER_COLOR, Win32.DWMWA_COLOR_NONE, 4);
        }
        _root.RequestedTheme = platform.RequestedTheme;
        ApplyCaption();
        _root.KeyDown += OnRootKeyDown;
        _root.Tag = host.Client;
        _xaml = new DesktopWindowXamlSource();
        _xaml.Initialize(Win32Interop.GetWindowIdFromWindow(Handle));
        _xaml.Content = _root;
        _xaml.SiteBridge.Show();
        platform.ThemeChanged += OnThemeChanged;
    }

    /// <summary>Sizes the island to the window's client area, in physical pixels.</summary>
    internal void FitIsland(int width, int height) => _xaml?.SiteBridge.MoveAndResize(new RectInt32(0, 0, width, height));

    private void OnThemeChanged()
    {
        _root.RequestedTheme = platform.RequestedTheme;
        ApplyCaption();
        if (_backdropConfig is { } config)
        {
            config.Theme = platform.BackdropTheme;
            config.IsHighContrast = platform.IsHighContrast;
        }
    }

    /// <summary>The caption follows the shell's dark or light choice, including a window that has a title bar.</summary>
    private void ApplyCaption()
    {
        if (Handle == 0)
            return;
        var dark = platform.IsDark ? 1 : 0;
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, dark, 4);
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && host.Kind == WindowKind.Popup && host.Placement.Popup?.GrabFocus == true)
        {
            host.Client.OnDismissed();
            e.Handled = true;
        }
    }

    /// <summary>Fades the content. The material behind it does not fade, and has no opacity of its own.</summary>
    public void SetOpacity(double opacity) => _root.Opacity = opacity;

    /// <summary>Clips the content to a rectangle in logical window coordinates. It does not change <see cref="HostPlacement.SetInputRegion"/>.</summary>
    public void SetVisibleMask(Rect? bounds) => _root.Clip = bounds is { } b
        ? new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(b.X, b.Y, b.Width, b.Height) }
        : null;

    /// <summary>
    /// Puts a material behind the content. A controller told the window is active keeps a live material on a window
    /// that never activates, which the DWM backdrop attribute does not.
    /// </summary>
    public void SetBackdrop(WindowBackdrop backdrop, WindowCorners corners)
    {
        _backdropController?.Dispose();
        _backdropController = null;
        _backdropConfig = null;
        if (backdrop != WindowBackdrop.None && _xaml is not null)
        {
            _backdropConfig = new SystemBackdropConfiguration { IsInputActive = true, Theme = platform.BackdropTheme, IsHighContrast = platform.IsHighContrast };
            ISystemBackdropControllerWithTargets controller = backdrop switch
            {
                WindowBackdrop.Acrylic => new DesktopAcrylicController(),
                WindowBackdrop.MicaAlt => new MicaController { Kind = MicaKind.BaseAlt },
                _ => new MicaController(),
            };
            controller.AddSystemBackdropTarget(_xaml.As<ICompositionSupportsSystemBackdrop>());
            controller.SetSystemBackdropConfiguration(_backdropConfig);
            _backdropController = controller;
        }
        var corner = corners switch
        {
            WindowCorners.Square => 1,
            WindowCorners.Round => 2,
            WindowCorners.RoundSmall => 3,
            // A bar along an edge stays square; a popup with a material is a rounded flyout.
            _ => host.Kind switch
            {
                WindowKind.Floating => 0,
                WindowKind.Popup when backdrop != WindowBackdrop.None => 2,
                _ => 1,
            },
        };
        if (host.Placement.Frame is { } frame)
            frame.SetCorners(corner);
        else
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, corner, 4);
        if (host.Kind != WindowKind.Floating)
        {
            // A material window gets Windows' hairline border; a plain one gets none.
            var border = host.Kind == WindowKind.Popup && backdrop != WindowBackdrop.None ? 0xFFFFFFFF : Win32.DWMWA_COLOR_NONE;
            Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_BORDER_COLOR, border, 4);
        }
    }

    public void SetKeyboardFocus(bool focus)
    {
        if (!focus)
            return;
        if (host.Kind == WindowKind.Panel && host.Placement.Panel?.Focusable != true)
            return;
        // A Windows-key chord arrives through a low-level hook, which does not grant foreground rights, so
        // SetForegroundWindow can fail. Join the foreground thread long enough to take the keyboard, then leave it.
        if (Win32.GetForegroundWindow() != Handle && !Win32.SetForegroundWindow(Handle))
            JoinForegroundAndTakeIt();
        FocusContent();
    }

    /// <summary>Hands the keyboard to the island, unless it already has it.</summary>
    internal void FocusContent()
    {
        if (_xaml is { HasFocus: false } xaml)
            xaml.NavigateFocus(new XamlSourceFocusNavigationRequest(XamlSourceFocusNavigationReason.Programmatic));
    }

    private void JoinForegroundAndTakeIt()
    {
        var foreground = Win32.GetForegroundWindow();
        var other = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
        var current = Win32.GetCurrentThreadId();
        if (other == 0 || other == current || !Win32.AttachThreadInput(current, other, true))
            return;
        try
        {
            Win32.SetForegroundWindow(Handle);
        }
        finally
        {
            Win32.AttachThreadInput(current, other, false);
        }
    }

    public void Dispose()
    {
        platform.ThemeChanged -= OnThemeChanged;
        _backdropController?.Dispose();
        _backdropController = null;
        _root.KeyDown -= OnRootKeyDown;
        _root.Children.Clear();
        _xaml?.Dispose();
        _xaml = null;
    }
}
