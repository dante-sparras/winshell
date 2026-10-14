using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Winshell;

/// <summary>Pins the bar to a screen edge.</summary>
internal static class BarPlacement
{
    /// <summary>Top and bottom bar height, in device-independent pixels. The Windows 11 taskbar is 48.</summary>
    public const int Horizontal = 48;

    /// <summary>Left and right bar thickness, in device-independent pixels. Same face as the horizontal bar.</summary>
    public const int Vertical = 48;

    public static int Thickness(BarEdge edge) =>
        edge is BarEdge.Left or BarEdge.Right ? Vertical : Horizontal;
    public static void Present(AppWindow window)
    {
        Log.Information("Bar", "bar presenter set");
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        window.SetPresenter(presenter);
        window.TitleBar.ExtendsContentIntoTitleBar = true;
        window.TitleBar.SetDragRectangles([]);
    }

    /// <summary>Puts a Bar window on <paramref name="edge"/> of <paramref name="screen"/>, as thick as the window's own scale makes it.</summary>
    /// <param name="window">The Bar's window.</param>
    /// <param name="hwnd">Its handle.</param>
    /// <param name="screen">The monitor the Bar is on, whole, in physical pixels.</param>
    /// <param name="edge">The edge every Bar is on.</param>
    /// <param name="frameColor">The color Windows paints the window's frame in.</param>
    public static void Move(AppWindow window, nint hwnd, ShellRect screen, BarEdge edge, int frameColor)
    {
        var occupied = BarStrip.Place(screen, edge, ScreenMetrics.Dips(hwnd, Thickness(edge)));
        var target = new RectInt32(occupied.X, occupied.Y, occupied.Width, occupied.Height);
        var position = window.Position;
        var size = window.Size;
        var moved = position.X != target.X || position.Y != target.Y || size.Width != target.Width || size.Height != target.Height;
        if (moved)
        {
            window.MoveAndResize(target);
        }

        BorderlessFrame.Quiet(hwnd, frameColor, moved);
    }
}

