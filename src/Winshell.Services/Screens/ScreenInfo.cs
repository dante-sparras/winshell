using Windows.Foundation;

namespace Winshell;

/// <summary>
/// A monitor, as the platform reports it. Coordinates are logical pixels on the virtual desktop: a monitor keeps its
/// physical top-left corner and its size is divided by its scale, so monitors of mixed DPI never overlap.
/// </summary>
/// <param name="Id">The Windows display device name, such as <c>\\.\DISPLAY1</c>.</param>
/// <param name="Name">The display device name without its <c>\\.\</c> prefix, such as <c>DISPLAY1</c>.</param>
/// <param name="Model">The monitor's name as Windows lists it, such as <c>Generic PnP Monitor</c>. Can be empty.</param>
/// <param name="Manufacturer">The three-letter PnP code of the monitor's maker, such as <c>GSM</c>. Can be empty.</param>
/// <param name="SerialNumber">
/// The instance part of the monitor's Windows device id, which tells two monitors of one model apart. Can be empty.
/// </param>
/// <param name="Geometry">The whole monitor.</param>
/// <param name="WorkArea">The monitor without the taskbar and the edges panels reserve.</param>
/// <param name="DevicePixelRatio">The monitor's scale: physical pixels per logical pixel, such as 1.5 at 150%.</param>
/// <param name="PhysicalWidthMm">The width of the picture, in millimeters, as the monitor reports it. 0 when unknown.</param>
/// <param name="PhysicalHeightMm">The height of the picture, in millimeters, as the monitor reports it. 0 when unknown.</param>
/// <param name="RefreshRate">The refresh rate in hertz, or 60 when Windows does not say.</param>
/// <param name="IsPrimary">Whether this is the primary monitor, which Windows puts at the origin.</param>
public sealed record ScreenInfo(
    string Id,
    string Name,
    string Model,
    string Manufacturer,
    string SerialNumber,
    Rect Geometry,
    Rect WorkArea,
    double DevicePixelRatio,
    double PhysicalWidthMm,
    double PhysicalHeightMm,
    int RefreshRate,
    bool IsPrimary);
