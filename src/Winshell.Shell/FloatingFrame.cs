using Windows.Foundation;
using Winshell.Native;
using Winshell.Windows;

namespace Winshell;

/// <summary>How a floating window sits on its screen: framed at a size, minimized, maximized or fullscreen.</summary>
internal sealed unsafe class FloatingFrame(HostWindow host, ShellPlatform platform)
{
    private FloatingPlacement? _placement;
    private bool _placed;
    private bool _fullscreen;
    private bool _applying;
    private SavedFloatingPlace? _saved;
    private int _corners;
    private double _lastWidth = -1, _lastHeight = -1;

    /// <summary>Whether the window covers its monitor without a frame.</summary>
    public bool Fullscreen => _fullscreen;

    private nint Handle => host.Handle;

    public void Apply(FloatingPlacement placement)
    {
        _placement = placement;
        _applying = true;
        try
        {
            Win32.SetWindowTextW(Handle, placement.Title);
            OwnedWindows.Link(Handle, placement.Owner);
            if (_fullscreen && !placement.Fullscreen)
                ExitFullscreen(placement);
            else if (!_fullscreen && placement.Fullscreen)
            {
                ApplyFrame(placement);
                EnterFullscreen();
            }
            else if (_fullscreen)
            {
                // A size change while fullscreen is part of the saved place, not a new one.
                _lastWidth = placement.Width;
                _lastHeight = placement.Height;
                if (placement.Minimized && !Win32.IsIconic(Handle))
                    Win32.ShowWindow(Handle, Win32.SW_MINIMIZE);
                else if (!placement.Minimized && Win32.IsIconic(Handle))
                {
                    Win32.ShowWindow(Handle, Win32.SW_RESTORE);
                    FillMonitor();
                }
                else if (!Win32.IsIconic(Handle))
                    FillMonitor();
            }
            else
            {
                ApplyFrame(placement);
            }
            // Showing or restoring the owner can put it above the windows it owns. Put those back above it.
            if (!Win32.IsIconic(Handle))
                OwnedWindows.Raise(Handle, platform.Windows);
            ReportState();
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>The corners the window has when it is not fullscreen, which meets the monitor's corners.</summary>
    public void SetCorners(int preference)
    {
        _corners = preference;
        ApplyCorners(_fullscreen ? 1 : preference);
    }

    /// <summary>The window moved or changed size, which may be the user maximizing, minimizing or restoring it.</summary>
    public void OnMovedOrResized()
    {
        if (!_applying)
            ReportState();
    }

    /// <summary>The window's monitor changed scale, or the window went to another monitor.</summary>
    public void OnDpiChanged(RECT suggested, bool placing)
    {
        if (_fullscreen)
        {
            // The suggested rect is the old logical size. A fullscreen window keeps covering the monitor.
            if (!placing)
                FillMonitor();
        }
        else
        {
            // The user dragged it to another monitor, or that monitor's scale changed: Windows' rect keeps the logical size.
            Win32.SetWindowPos(Handle, 0, suggested.Left, suggested.Top, suggested.Width, suggested.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }
    }

    public void OnDisplayChanged()
    {
        if (_fullscreen)
            FillMonitor();
    }

    /// <summary>Sets the window's track sizes. Returns null to leave Windows' defaults.</summary>
    public nint? OnGetMinMaxInfo(MINMAXINFO* info)
    {
        if (_fullscreen && Handle != 0)
        {
            // The default maximum is the work area, which would stop the window short of the taskbar.
            var (_, bounds) = Screens.Of(Handle);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return null;
            info->ptMaxPosition.X = bounds.Left;
            info->ptMaxPosition.Y = bounds.Top;
            info->ptMaxSize.X = bounds.Width;
            info->ptMaxSize.Y = bounds.Height;
            info->ptMaxTrackSize.X = bounds.Width;
            info->ptMaxTrackSize.Y = bounds.Height;
            return 0;
        }
        if (_placement is not { } f)
            return null;
        if (f.MinimumSize.Width > 0)
            info->ptMinTrackSize.X = (int)(f.MinimumSize.Width * host.Scale);
        if (f.MinimumSize.Height > 0)
            info->ptMinTrackSize.Y = (int)(f.MinimumSize.Height * host.Scale);
        if (f.MaximumSize.Width > 0)
            info->ptMaxTrackSize.X = (int)(f.MaximumSize.Width * host.Scale);
        if (f.MaximumSize.Height > 0)
            info->ptMaxTrackSize.Y = (int)(f.MaximumSize.Height * host.Scale);
        return 0;
    }

    private void ApplyFrame(FloatingPlacement placement)
    {
        var first = !_placed;
        if (first || placement.Width != _lastWidth || placement.Height != _lastHeight)
        {
            _lastWidth = placement.Width;
            _lastHeight = placement.Height;
            var dpi = Win32.GetDpiForWindow(Handle);
            host.Scale = dpi / 96.0;
            var client = new RECT(0, 0, (int)Math.Round(placement.Width * host.Scale), (int)Math.Round(placement.Height * host.Scale));
            var (style, ex) = HostWindow.Styles(WindowKind.Floating);
            Win32.AdjustWindowRectExForDpi(ref client, style, false, ex, dpi);
            int x, y;
            if (first)
            {
                var area = Screens.Find(placement.Screen?.Id ?? platform.PrimaryScreenId)?.Area ?? default;
                x = area.Left + (area.Width - client.Width) / 2;
                y = area.Top + (area.Height - client.Height) / 2;
            }
            else
            {
                Win32.GetWindowRect(Handle, out var current);
                x = current.Left;
                y = current.Top;
            }
            Win32.SetWindowPos(Handle, 0, x, y, client.Width, client.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            _placed = true;
        }
        if (host.IsVisible)
        {
            if (placement.Minimized && !Win32.IsIconic(Handle))
                Win32.ShowWindow(Handle, Win32.SW_MINIMIZE);
            else if (placement.Maximized && !Win32.IsZoomed(Handle))
                Win32.ShowWindow(Handle, Win32.SW_SHOWMAXIMIZED);
            else if (!placement.Maximized && !placement.Minimized && (Win32.IsZoomed(Handle) || Win32.IsIconic(Handle)))
                Win32.ShowWindow(Handle, Win32.SW_RESTORE);
        }
    }

    private void EnterFullscreen()
    {
        _saved = CapturePlace();
        _fullscreen = true;
        SetStyle(BorderlessStyle());
        // Square corners, so the window meets the monitor's corners instead of rounding inside them.
        ApplyCorners(1);
        if (Win32.IsIconic(Handle) || Win32.IsZoomed(Handle))
            Win32.ShowWindow(Handle, Win32.SW_RESTORE);
        FillMonitor();
    }

    private void ExitFullscreen(FloatingPlacement placement)
    {
        var saved = _saved;
        _fullscreen = false;
        _saved = null;
        _lastWidth = placement.Width;
        _lastHeight = placement.Height;
        ApplyCorners(_corners);
        if (saved is null)
        {
            ApplyFrame(placement);
            return;
        }
        SetStyle(saved.Style);
        Reframe();
        var monitor = Screens.Find(saved.MonitorId) ?? CurrentMonitor();
        var scale = host.Scale > 0 ? host.Scale : saved.Scale;
        if (monitor is not null && saved.Matches(monitor.Info.Id, monitor.Bounds, scale) && saved.HasPlacement)
        {
            var next = saved.Placement;
            next.ShowCmd = placement.StateRequested
                ? RequestedShow(placement)
                : Shown(saved);
            if (placement.StateRequested && !placement.Maximized)
                next.Flags &= ~Win32.WPF_RESTORETOMAXIMIZED;
            if (Win32.SetWindowPlacement(Handle, next))
            {
                host.UpdateSize();
                return;
            }
        }
        if (monitor is null)
        {
            ApplyFrame(placement);
            return;
        }
        var rect = saved.RectOn(monitor.Bounds, scale);
        PlaceOuter(rect);
        if (!host.IsVisible)
            return;
        var maximized = placement.StateRequested ? placement.Maximized : saved.Maximized;
        var minimized = placement.StateRequested ? placement.Minimized : saved.Minimized;
        if (minimized)
            Win32.ShowWindow(Handle, Win32.SW_MINIMIZE);
        else if (maximized)
            Win32.ShowWindow(Handle, Win32.SW_SHOWMAXIMIZED);
        else if (Win32.IsZoomed(Handle) || Win32.IsIconic(Handle))
            Win32.ShowWindow(Handle, Win32.SW_RESTORE);
    }

    private uint Shown(SavedFloatingPlace saved)
    {
        if (!host.IsVisible)
            return (uint)Win32.SW_HIDE;
        if (saved.Minimized)
            return (uint)Win32.SW_SHOWMINIMIZED;
        if (saved.Maximized)
            return (uint)Win32.SW_SHOWMAXIMIZED;
        var show = saved.HasPlacement ? saved.Placement.ShowCmd : 0;
        // A window that was never shown has no show command. Leaving fullscreen while it is visible shows it.
        return show == 0 ? (uint)Win32.SW_SHOWNORMAL : show;
    }

    private static uint RequestedShow(FloatingPlacement placement) =>
        (uint)(placement.Minimized ? Win32.SW_SHOWMINIMIZED : placement.Maximized ? Win32.SW_SHOWMAXIMIZED : Win32.SW_SHOWNORMAL);

    private SavedFloatingPlace CapturePlace()
    {
        var monitor = CurrentMonitor();
        var dpi = Win32.GetDpiForWindow(Handle);
        var scale = dpi == 0 ? monitor?.Scale ?? host.Scale : dpi / 96.0;
        if (scale <= 0)
            scale = 1;
        var bounds = monitor?.Bounds ?? default;
        Win32.GetWindowRect(Handle, out var shown);
        var maximized = Win32.IsZoomed(Handle);
        var minimized = Win32.IsIconic(Handle);
        var placement = new Winshell.Native.WINDOWPLACEMENT { Length = (uint)sizeof(Winshell.Native.WINDOWPLACEMENT) };
        var hasPlacement = Win32.GetWindowPlacement(Handle, ref placement);
        var normal = shown;
        if ((maximized || minimized) && hasPlacement)
            normal = RestoredScreenRect(placement, shown);
        var logical = bounds.Width > 0 ? DesktopSpace.ToLogical(normal, bounds, scale) : new Rect(0, 0, normal.Width, normal.Height);
        return new SavedFloatingPlace(
            (uint)Win32.GetWindowLongPtr(Handle, Win32.GWL_STYLE),
            scale,
            bounds,
            monitor?.Info.Id ?? "",
            logical.X - bounds.Left,
            logical.Y - bounds.Top,
            logical.Width,
            logical.Height,
            maximized,
            minimized,
            placement,
            hasPlacement);
    }

    /// <summary>
    /// The restored rectangle in screen pixels. A normal window is already there. A minimized or maximized one
    /// reports it in workspace coordinates, which start at the primary monitor's work area.
    /// </summary>
    private static RECT RestoredScreenRect(Winshell.Native.WINDOWPLACEMENT placement, RECT shown)
    {
        var primary = Screens.Enumerate().FirstOrDefault(m => m.Info.IsPrimary);
        var bias = primary is null
            ? (X: 0, Y: 0)
            : WindowRules.WorkspaceOffset(primary.Bounds, primary.Work, false);
        var shifted = WindowRules.Shift(placement.Normal, bias.X, bias.Y);
        return shifted.Width > 0 && shifted.Height > 0 ? shifted : shown;
    }

    private uint BorderlessStyle()
    {
        var style = (uint)Win32.GetWindowLongPtr(Handle, Win32.GWL_STYLE);
        var next = (style & ~Win32.WS_OVERLAPPEDWINDOW) | Win32.WS_POPUP | Win32.WS_CLIPCHILDREN;
        if ((style & Win32.WS_VISIBLE) != 0)
            next |= Win32.WS_VISIBLE;
        return next;
    }

    private void SetStyle(uint style)
    {
        if (Handle == 0)
            return;
        var current = (uint)Win32.GetWindowLongPtr(Handle, Win32.GWL_STYLE);
        // Showing and hiding go through ShowWindow. A frame change keeps whichever of those the window is in.
        if ((current & Win32.WS_VISIBLE) != 0)
            style |= Win32.WS_VISIBLE;
        else
            style &= ~Win32.WS_VISIBLE;
        if (current == style)
            return;
        Win32.SetWindowLongPtr(Handle, Win32.GWL_STYLE, (nint)style);
    }

    private void Reframe() =>
        Win32.SetWindowPos(Handle, 0, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);

    private void FillMonitor()
    {
        if (Handle == 0)
            return;
        var (_, bounds) = Screens.Of(Handle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        PlaceOuter(bounds);
    }

    private void PlaceOuter(RECT rect) => host.Move(rect, 0, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);

    private void ApplyCorners(int preference) =>
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, preference, 4);

    private ConnectedMonitor? CurrentMonitor()
    {
        if (Handle == 0)
            return null;
        var handle = Win32.MonitorFromWindow(Handle, Win32.MONITOR_DEFAULTTONEAREST);
        return handle == 0 ? null : Screens.Describe(handle);
    }

    private void ReportState()
    {
        if (Handle == 0)
            return;
        var fullscreen = ReadsFullscreen();
        _fullscreen = fullscreen;
        host.Client.OnFullscreenChanged(fullscreen);
        // A fullscreen window covers the monitor, not the work area, so it is not the framed maximized state.
        host.Client.OnStateChanged(Win32.IsZoomed(Handle) && !fullscreen, Win32.IsIconic(Handle));
    }

    private bool ReadsFullscreen()
    {
        if (Win32.IsIconic(Handle))
            return _fullscreen;
        var style = (uint)Win32.GetWindowLongPtr(Handle, Win32.GWL_STYLE);
        if ((style & (Win32.WS_CAPTION | Win32.WS_THICKFRAME)) != 0)
            return false;
        if (!Win32.GetWindowRect(Handle, out var rect))
            return _fullscreen;
        var (_, bounds) = Screens.Of(Handle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return _fullscreen;
        return rect.Left <= bounds.Left + 1 && rect.Top <= bounds.Top + 1 && rect.Right >= bounds.Right - 1 && rect.Bottom >= bounds.Bottom - 1;
    }
}
