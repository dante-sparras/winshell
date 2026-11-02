using System.Globalization;
using Windows.UI;

namespace Winshell;

/// <summary>
/// The lighter and darker shades of an accent Winshell picked. Windows does not say how it makes them, so each keeps
/// the hue and saturation and moves the lightness as far as the default blue's shades do.
/// </summary>
internal readonly record struct AccentShades(Color Accent, Color Light1, Color Light2, Color Light3, Color Dark1, Color Dark2, Color Dark3)
{
    private static readonly double[] Lighter = [0.123, 0.466, 0.654];
    private static readonly double[] Darker = [0.132, 0.397, 0.69];

    internal static string Normalize(string? hex) => TryParse(hex, out _, out _, out _) ? hex!.ToUpperInvariant() : "";

    internal static bool TryFrom(string? hex, out AccentShades shades)
    {
        if (!TryParse(hex, out var r, out var g, out var b))
        {
            shades = default;
            return false;
        }
        var (h, s, l) = ToHsl(r, g, b);
        shades = new(
            Rgb(r, g, b),
            FromHsl(h, s, l + (1 - l) * Lighter[0]),
            FromHsl(h, s, l + (1 - l) * Lighter[1]),
            FromHsl(h, s, l + (1 - l) * Lighter[2]),
            FromHsl(h, s, l * (1 - Darker[0])),
            FromHsl(h, s, l * (1 - Darker[1])),
            FromHsl(h, s, l * (1 - Darker[2])));
        return true;
    }

    private static bool TryParse(string? hex, out double r, out double g, out double b)
    {
        r = g = b = 0;
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !byte.TryParse(hex.AsSpan(1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rb)
            || !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var gb)
            || !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var bb))
            return false;
        r = rb / 255d;
        g = gb / 255d;
        b = bb / 255d;
        return true;
    }

    private static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var l = (max + min) / 2;
        var h = d == 0 ? -1 : max == r ? ((g - b) / d % 6 + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        var s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        return (h < 0 ? -1 : h / 6, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        if (h < 0)
            return Rgb(l, l, l);
        h = h % 1 * 6;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Rgb(r + m, g + m, b + m);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private static Color Rgb(double r, double g, double b) => Color.FromArgb(255, ToByte(r), ToByte(g), ToByte(b));
}
