using System.Globalization;

namespace Winshell.Runtime;

/// <summary>A QML color. Components are 0 to 1. <see cref="IsValid"/> is false for the default value.</summary>
public readonly record struct QColor(double R, double G, double B, double A, bool IsValid = true)
{
    public static readonly QColor Invalid = new(0, 0, 0, 0, false);
    public static readonly QColor Transparent = new(0, 0, 0, 0);
    public static readonly QColor Black = new(0, 0, 0, 1);
    public static readonly QColor White = new(1, 1, 1, 1);

    public static QColor FromRgb(byte r, byte g, byte b, byte a = 255) => new(r / 255.0, g / 255.0, b / 255.0, a / 255.0);

    public static QColor FromArgb(uint argb) =>
        FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    public byte R8 => ToByte(R);

    public byte G8 => ToByte(G);

    public byte B8 => ToByte(B);

    public byte A8 => ToByte(A);

    public uint Argb => ((uint)A8 << 24) | ((uint)R8 << 16) | ((uint)G8 << 8) | B8;

    public double HsvHue => ToHsv().H;

    public double HsvSaturation => ToHsv().S;

    public double HsvValue => ToHsv().V;

    public double HslHue => ToHsl().H;

    public double HslSaturation => ToHsl().S;

    public double HslLightness => ToHsl().L;

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

    /// <summary>"#rrggbb", or "#aarrggbb" when not opaque, like Qt.</summary>
    public override string ToString() =>
        A8 == 255
            ? $"#{R8:x2}{G8:x2}{B8:x2}"
            : $"#{A8:x2}{R8:x2}{G8:x2}{B8:x2}";

    public QColor WithAlpha(double a) => this with { A = a };

    /// <summary>The color with its alpha scaled by an opacity, as an item's opacity scales what it draws.</summary>
    public QColor Faded(double opacity) => this with { A = A * opacity };

    public (double H, double S, double V) ToHsv()
    {
        var max = Math.Max(R, Math.Max(G, B));
        var min = Math.Min(R, Math.Min(G, B));
        var d = max - min;
        var h = d == 0 ? -1 : max == R ? ((G - B) / d % 6 + 6) % 6 : max == G ? (B - R) / d + 2 : (R - G) / d + 4;
        return (h < 0 ? -1 : h / 6, max == 0 ? 0 : d / max, max);
    }

    public (double H, double S, double L) ToHsl()
    {
        var max = Math.Max(R, Math.Max(G, B));
        var min = Math.Min(R, Math.Min(G, B));
        var d = max - min;
        var l = (max + min) / 2;
        var h = d == 0 ? -1 : max == R ? ((G - B) / d % 6 + 6) % 6 : max == G ? (B - R) / d + 2 : (R - G) / d + 4;
        var s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        return (h < 0 ? -1 : h / 6, s, l);
    }

    public static QColor FromHsv(double h, double s, double v, double a = 1)
    {
        if (h < 0)
            return new QColor(v, v, v, a);
        h = h % 1 * 6;
        var c = v * s;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = v - c;
        var (r, g, b) = Sector(h, c, x);
        return new QColor(r + m, g + m, b + m, a);
    }

    public static QColor FromHsl(double h, double s, double l, double a = 1)
    {
        if (h < 0)
            return new QColor(l, l, l, a);
        h = h % 1 * 6;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = Sector(h, c, x);
        return new QColor(r + m, g + m, b + m, a);
    }

    private static (double, double, double) Sector(double h, double c, double x) => h switch
    {
        < 1 => (c, x, 0),
        < 2 => (x, c, 0),
        < 3 => (0, c, x),
        < 4 => (0, x, c),
        < 5 => (x, 0, c),
        _ => (c, 0, x),
    };

    /// <summary>Qt.lighter: scales the HSV value by the factor.</summary>
    public QColor Lighter(double factor = 1.5)
    {
        if (factor <= 0)
            return this;
        var (h, s, v) = ToHsv();
        v *= factor;
        if (v > 1)
        {
            s -= v - 1;
            if (s < 0)
                s = 0;
            v = 1;
        }
        return FromHsv(h, s, v, A);
    }

    /// <summary>Qt.darker: divides the HSV value by the factor.</summary>
    public QColor Darker(double factor = 2.0)
    {
        if (factor <= 0)
            return this;
        var (h, s, v) = ToHsv();
        return FromHsv(h, s, v / factor, A);
    }

    public static QColor Tint(QColor baseColor, QColor tint)
    {
        var a = tint.A;
        if (a <= 0)
            return baseColor;
        if (a >= 1)
            return tint;
        var outA = a + baseColor.A * (1 - a);
        return new QColor(
            (tint.R * a + baseColor.R * baseColor.A * (1 - a)) / outA,
            (tint.G * a + baseColor.G * baseColor.A * (1 - a)) / outA,
            (tint.B * a + baseColor.B * baseColor.A * (1 - a)) / outA,
            outA);
    }

    public static QColor Blend(QColor a, QColor b, double t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    public static bool TryParse(string? text, out QColor color)
    {
        color = Invalid;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var s = text.Trim();
        if (s[0] == '#')
        {
            var hex = s[1..];
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return false;
            switch (hex.Length)
            {
                case 3:
                    color = FromRgb((byte)((v >> 8 & 0xf) * 17), (byte)((v >> 4 & 0xf) * 17), (byte)((v & 0xf) * 17));
                    return true;
                case 4:
                    color = FromRgb((byte)((v >> 8 & 0xf) * 17), (byte)((v >> 4 & 0xf) * 17), (byte)((v & 0xf) * 17), (byte)((v >> 12 & 0xf) * 17));
                    return true;
                case 6:
                    color = FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
                    return true;
                case 8:
                    color = FromArgb(v);
                    return true;
                default:
                    return false;
            }
        }
        if (NamedColors.TryGetValue(s.ToLowerInvariant(), out var named))
        {
            color = FromArgb(named);
            return true;
        }
        return false;
    }

    public static QColor Parse(string text) =>
        TryParse(text, out var c) ? c : throw new QmlException($"Invalid color \"{text}\"");

    private static readonly Dictionary<string, uint> NamedColors = new(StringComparer.Ordinal)
    {
        ["transparent"] = 0x00000000,
        ["aliceblue"] = 0xfff0f8ff, ["antiquewhite"] = 0xfffaebd7, ["aqua"] = 0xff00ffff, ["aquamarine"] = 0xff7fffd4,
        ["azure"] = 0xfff0ffff, ["beige"] = 0xfff5f5dc, ["bisque"] = 0xffffe4c4, ["black"] = 0xff000000,
        ["blanchedalmond"] = 0xffffebcd, ["blue"] = 0xff0000ff, ["blueviolet"] = 0xff8a2be2, ["brown"] = 0xffa52a2a,
        ["burlywood"] = 0xffdeb887, ["cadetblue"] = 0xff5f9ea0, ["chartreuse"] = 0xff7fff00, ["chocolate"] = 0xffd2691e,
        ["coral"] = 0xffff7f50, ["cornflowerblue"] = 0xff6495ed, ["cornsilk"] = 0xfffff8dc, ["crimson"] = 0xffdc143c,
        ["cyan"] = 0xff00ffff, ["darkblue"] = 0xff00008b, ["darkcyan"] = 0xff008b8b, ["darkgoldenrod"] = 0xffb8860b,
        ["darkgray"] = 0xffa9a9a9, ["darkgreen"] = 0xff006400, ["darkgrey"] = 0xffa9a9a9, ["darkkhaki"] = 0xffbdb76b,
        ["darkmagenta"] = 0xff8b008b, ["darkolivegreen"] = 0xff556b2f, ["darkorange"] = 0xffff8c00, ["darkorchid"] = 0xff9932cc,
        ["darkred"] = 0xff8b0000, ["darksalmon"] = 0xffe9967a, ["darkseagreen"] = 0xff8fbc8f, ["darkslateblue"] = 0xff483d8b,
        ["darkslategray"] = 0xff2f4f4f, ["darkslategrey"] = 0xff2f4f4f, ["darkturquoise"] = 0xff00ced1, ["darkviolet"] = 0xff9400d3,
        ["deeppink"] = 0xffff1493, ["deepskyblue"] = 0xff00bfff, ["dimgray"] = 0xff696969, ["dimgrey"] = 0xff696969,
        ["dodgerblue"] = 0xff1e90ff, ["firebrick"] = 0xffb22222, ["floralwhite"] = 0xfffffaf0, ["forestgreen"] = 0xff228b22,
        ["fuchsia"] = 0xffff00ff, ["gainsboro"] = 0xffdcdcdc, ["ghostwhite"] = 0xfff8f8ff, ["gold"] = 0xffffd700,
        ["goldenrod"] = 0xffdaa520, ["gray"] = 0xff808080, ["grey"] = 0xff808080, ["green"] = 0xff008000,
        ["greenyellow"] = 0xffadff2f, ["honeydew"] = 0xfff0fff0, ["hotpink"] = 0xffff69b4, ["indianred"] = 0xffcd5c5c,
        ["indigo"] = 0xff4b0082, ["ivory"] = 0xfffffff0, ["khaki"] = 0xfff0e68c, ["lavender"] = 0xffe6e6fa,
        ["lavenderblush"] = 0xfffff0f5, ["lawngreen"] = 0xff7cfc00, ["lemonchiffon"] = 0xfffffacd, ["lightblue"] = 0xffadd8e6,
        ["lightcoral"] = 0xfff08080, ["lightcyan"] = 0xffe0ffff, ["lightgoldenrodyellow"] = 0xfffafad2, ["lightgray"] = 0xffd3d3d3,
        ["lightgreen"] = 0xff90ee90, ["lightgrey"] = 0xffd3d3d3, ["lightpink"] = 0xffffb6c1, ["lightsalmon"] = 0xffffa07a,
        ["lightseagreen"] = 0xff20b2aa, ["lightskyblue"] = 0xff87cefa, ["lightslategray"] = 0xff778899, ["lightslategrey"] = 0xff778899,
        ["lightsteelblue"] = 0xffb0c4de, ["lightyellow"] = 0xffffffe0, ["lime"] = 0xff00ff00, ["limegreen"] = 0xff32cd32,
        ["linen"] = 0xfffaf0e6, ["magenta"] = 0xffff00ff, ["maroon"] = 0xff800000, ["mediumaquamarine"] = 0xff66cdaa,
        ["mediumblue"] = 0xff0000cd, ["mediumorchid"] = 0xffba55d3, ["mediumpurple"] = 0xff9370db, ["mediumseagreen"] = 0xff3cb371,
        ["mediumslateblue"] = 0xff7b68ee, ["mediumspringgreen"] = 0xff00fa9a, ["mediumturquoise"] = 0xff48d1cc, ["mediumvioletred"] = 0xffc71585,
        ["midnightblue"] = 0xff191970, ["mintcream"] = 0xfff5fffa, ["mistyrose"] = 0xffffe4e1, ["moccasin"] = 0xffffe4b5,
        ["navajowhite"] = 0xffffdead, ["navy"] = 0xff000080, ["oldlace"] = 0xfffdf5e6, ["olive"] = 0xff808000,
        ["olivedrab"] = 0xff6b8e23, ["orange"] = 0xffffa500, ["orangered"] = 0xffff4500, ["orchid"] = 0xffda70d6,
        ["palegoldenrod"] = 0xffeee8aa, ["palegreen"] = 0xff98fb98, ["paleturquoise"] = 0xffafeeee, ["palevioletred"] = 0xffdb7093,
        ["papayawhip"] = 0xffffefd5, ["peachpuff"] = 0xffffdab9, ["peru"] = 0xffcd853f, ["pink"] = 0xffffc0cb,
        ["plum"] = 0xffdda0dd, ["powderblue"] = 0xffb0e0e6, ["purple"] = 0xff800080, ["rebeccapurple"] = 0xff663399,
        ["red"] = 0xffff0000, ["rosybrown"] = 0xffbc8f8f, ["royalblue"] = 0xff4169e1, ["saddlebrown"] = 0xff8b4513,
        ["salmon"] = 0xfffa8072, ["sandybrown"] = 0xfff4a460, ["seagreen"] = 0xff2e8b57, ["seashell"] = 0xfffff5ee,
        ["sienna"] = 0xffa0522d, ["silver"] = 0xffc0c0c0, ["skyblue"] = 0xff87ceeb, ["slateblue"] = 0xff6a5acd,
        ["slategray"] = 0xff708090, ["slategrey"] = 0xff708090, ["snow"] = 0xfffffafa, ["springgreen"] = 0xff00ff7f,
        ["steelblue"] = 0xff4682b4, ["tan"] = 0xffd2b48c, ["teal"] = 0xff008080, ["thistle"] = 0xffd8bfd8,
        ["tomato"] = 0xffff6347, ["turquoise"] = 0xff40e0d0, ["violet"] = 0xffee82ee, ["wheat"] = 0xfff5deb3,
        ["white"] = 0xffffffff, ["whitesmoke"] = 0xfff5f5f5, ["yellow"] = 0xffffff00, ["yellowgreen"] = 0xff9acd32,
    };
}

public readonly record struct QPoint(double X, double Y)
{
    public override string ToString() => FormattableString.Invariant($"QPointF({X}, {Y})");
}

public readonly record struct QSize(double Width, double Height)
{
    public override string ToString() => FormattableString.Invariant($"QSizeF({Width}, {Height})");
}

public readonly record struct QRect(double X, double Y, double Width, double Height)
{
    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    public bool IntersectsWith(QRect other) =>
        other.X < Right && other.Right > X && other.Y < Bottom && other.Bottom > Y;

    public override string ToString() => FormattableString.Invariant($"QRectF({X}, {Y}, {Width}, {Height})");
}
