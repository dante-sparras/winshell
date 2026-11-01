namespace Winshell;

/// <summary>
/// A monitor, as the platform reports it. Coordinates are logical pixels on the virtual desktop: a monitor keeps its
/// physical top-left corner and its size is divided by its scale, so monitors of mixed DPI never overlap.
/// </summary>
public sealed record ScreenInfo(
    string Id,
    string Name,
    string Model,
    string Manufacturer,
    string SerialNumber,
    QRect Geometry,
    QRect WorkArea,
    double DevicePixelRatio,
    double PhysicalWidthMm,
    double PhysicalHeightMm,
    int RefreshRate,
    bool IsPrimary);
