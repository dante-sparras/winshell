#pragma warning disable CA1416

using System.Runtime.InteropServices;

namespace Winshell.Runtime.Quick;

/// <summary>How DirectWrite should antialias a run of text.</summary>
public enum TextPixelMode
{
    Aliased,
    Grayscale,
    ClearType,
}

/// <summary>
/// Chooses ClearType or grayscale from the Windows font-smoothing settings. ClearType is not used where a
/// backdrop shows through, because its colored fringes only composite against an opaque surface.
/// </summary>
public static class TextRaster
{
    private const uint SpiGetFontSmoothing = 0x004A;
    private const uint SpiGetFontSmoothingType = 0x200A;
    private const int ClearTypeSmoothing = 2;

    private static readonly object Gate = new();
    private static int _smoothing = int.MinValue;
    private static int _type = int.MinValue;

    public static bool SmoothingEnabled => Read(ref _smoothing, SpiGetFontSmoothing) != 0;

    public static bool ClearTypeEnabled => Read(ref _type, SpiGetFontSmoothingType) == ClearTypeSmoothing;

    /// <summary>Drops the cached system settings. The next choice reads them again.</summary>
    public static void Refresh()
    {
        lock (Gate)
        {
            _smoothing = int.MinValue;
            _type = int.MinValue;
        }
    }

    public static TextPixelMode Choose(bool onBackdrop, bool antialias, bool? smoothing = null, bool? clearType = null)
    {
        var smooth = smoothing ?? SmoothingEnabled;
        var clear = clearType ?? ClearTypeEnabled;
        if (!antialias || !smooth)
            return TextPixelMode.Aliased;
        if (onBackdrop || !clear)
            return TextPixelMode.Grayscale;
        return TextPixelMode.ClearType;
    }

    private static int Read(ref int cache, uint action)
    {
        lock (Gate)
        {
            if (cache != int.MinValue)
                return cache;
            var value = 0;
            if (!SystemParametersInfo(action, 0, ref value, 0))
                value = action == SpiGetFontSmoothingType ? ClearTypeSmoothing : 1;
            cache = value;
            return value;
        }
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);
}
