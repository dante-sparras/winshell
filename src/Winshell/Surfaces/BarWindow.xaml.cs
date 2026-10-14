using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Winshell;

/// <summary>
/// One monitor's Bar: a window along an edge of that monitor (ADR 0080). Every monitor has one, and each is a full copy.
/// It holds only what is one Bar's own: its window, the monitor it is on, and whether it is in front of the windows there.
/// <see cref="DesktopLoop"/> makes, places, and layers the Bars, and <see cref="BarHost"/> draws the items on each (<see cref="BarView"/>).
/// </summary>
public sealed partial class BarWindow : Window
{
    /// <summary>Makes the window. It is not on an edge or on screen yet.</summary>
    public BarWindow()
    {
        InitializeComponent();
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    }

    /// <summary>The window's handle. A WinUI window keeps one handle for its life, and asking for it is a COM call.</summary>
    internal nint Hwnd { get; }

    /// <summary>The monitor this Bar is on, whole, in physical pixels.</summary>
    internal ShellRect Screen { get; set; }

    /// <summary>The cell every item of this Bar is laid out in.</summary>
    internal Grid Items => Slots;

    /// <summary>The mica behind this Bar. A backdrop serves one window.</summary>
    internal ShellBackdrop Backdrop { get; } = new(mica: true);

    /// <summary>The acrylic behind this Bar while the Host chose it. A backdrop serves one window.</summary>
    internal ShellBackdrop Acrylic { get; } = new();

    /// <summary>The color Windows paints this window's frame in, which <see cref="BarChrome"/> decides.</summary>
    internal int FrameColor { get; set; } = 0x00202020;

    /// <summary>True once the window has its presenter, which takes its caption and keeps it above the windows.</summary>
    internal bool Presented { get; set; }

    /// <summary>False until the window has been put on its edge of <see cref="Screen"/> as that monitor is now.</summary>
    internal bool Placed { get; set; }

    /// <summary>True while this Bar is behind a window that covers its monitor.</summary>
    internal bool Covered { get; set; }

    /// <summary>True while the window is written as not topmost, which a hidden Bar's look at the cover does not touch.</summary>
    internal bool Behind { get; set; }

    /// <summary>The window this Bar went behind, or zero.</summary>
    internal nint CoveredBy { get; set; }

    /// <summary>False while a fullscreen window on this Bar's monitor may cover a Panel open under this Bar (<c>BarLayer</c>).</summary>
    internal bool PanelAbove { get; set; } = true;

    /// <summary>When this Bar's layer was last looked at, on the tick clock.</summary>
    internal long SyncedAt { get; set; }

    /// <summary>True once the desktop is closing this window itself, because its monitor went.</summary>
    internal bool Leaving { get; set; }

    /// <summary>Sets the room between the window's edge and its items, for a Bar that runs along or across.</summary>
    /// <param name="vertical">True on a Bar along the left or right edge.</param>
    internal void Pad(bool vertical) => Root.Padding = Spacing.Bar(vertical);
}

