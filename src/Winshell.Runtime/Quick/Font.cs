using Jint;
using System.Collections.Concurrent;
using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>A font, as text layout needs it.</summary>
public readonly record struct FontSpec(string Family, double PixelSize, int Weight, bool Italic, bool Underline, bool Strikeout, double LetterSpacing, int Capitalization)
{
    /// <summary>
    /// The font text uses when none is set: Segoe UI Variable, the Windows 11 interface font, at the size
    /// Windows 11 uses for body text.
    /// </summary>
    public static string DefaultFamily { get; set; } = "Segoe UI Variable";

    public static double DefaultPixelSize { get; set; } = 14;

    public static FontSpec Default => new(DefaultFamily, DefaultPixelSize, 400, false, false, false, 0, 0);
}

/// <summary>An item's <c>font</c> group, or a font of its own, such as a step of the type ramp.</summary>
public sealed class FontGroup(Item? owner) : QObject
{
    private string? _family;
    private double _pixelSize = -1;
    private double _pointSize = -1;
    private int _weight = 400;
    private bool _italic;
    private bool _underline;
    private bool _strikeout;
    private double _letterSpacing;
    private double _wordSpacing;
    private int _capitalization;
    private int _hintingPreference;
    private string _styleName = "";
    private bool _kerning = true;
    private bool _preferShaping = true;

    public event Action? Changed;

    [QmlEnum]
    public enum Weights
    {
        Thin = 100,
        ExtraLight = 200,
        Light = 300,
        Normal = 400,
        Medium = 500,
        DemiBold = 600,
        Bold = 700,
        ExtraBold = 800,
        Black = 900,
    }

    private void Raise()
    {
        Changed?.Invoke();
        owner?.Update();
    }

    [Qml]
    public string Family
    {
        get { Track(); return _family ?? FontSpec.DefaultFamily; }
        set
        {
            if (SetField(ref _family, string.IsNullOrEmpty(value) ? null : value))
                Raise();
        }
    }

    [Qml]
    public double PixelSize
    {
        get { Track(); return _pixelSize > 0 ? _pixelSize : _pointSize > 0 ? Math.Round(_pointSize * 96 / 72) : FontSpec.DefaultPixelSize; }
        set
        {
            if (value <= 0 || !SetField(ref _pixelSize, value))
                return;
            _pointSize = -1;
            NotifyChanged("pointSize");
            Raise();
        }
    }

    [Qml]
    public double PointSize
    {
        get { Track(); return _pointSize > 0 ? _pointSize : PixelSize * 72 / 96; }
        set
        {
            if (value <= 0 || !SetField(ref _pointSize, value))
                return;
            _pixelSize = -1;
            NotifyChanged("pixelSize");
            Raise();
        }
    }

    [Qml]
    public int Weight
    {
        get { Track(); return _weight; }
        set
        {
            if (!SetField(ref _weight, Math.Clamp(value, 1, 1000)))
                return;
            NotifyChanged("bold");
            Raise();
        }
    }

    [Qml]
    public bool Bold
    {
        get { Track(); return _weight >= 600; }
        set => Weight = value ? 700 : 400;
    }

    [Qml]
    public bool Italic
    {
        get { Track(); return _italic; }
        set
        {
            if (SetField(ref _italic, value))
                Raise();
        }
    }

    [Qml]
    public bool Underline
    {
        get { Track(); return _underline; }
        set
        {
            if (SetField(ref _underline, value))
                Raise();
        }
    }

    [Qml]
    public bool Strikeout
    {
        get { Track(); return _strikeout; }
        set
        {
            if (SetField(ref _strikeout, value))
                Raise();
        }
    }

    [Qml]
    public double LetterSpacing
    {
        get { Track(); return _letterSpacing; }
        set
        {
            if (SetField(ref _letterSpacing, value))
                Raise();
        }
    }

    [Qml]
    public double WordSpacing
    {
        get { Track(); return _wordSpacing; }
        set
        {
            if (SetField(ref _wordSpacing, value))
                Raise();
        }
    }

    [Qml]
    public int Capitalization
    {
        get { Track(); return _capitalization; }
        set
        {
            if (SetField(ref _capitalization, value))
                Raise();
        }
    }

    [Qml]
    public int HintingPreference
    {
        get { Track(); return _hintingPreference; }
        set => SetField(ref _hintingPreference, value);
    }

    [Qml]
    public string StyleName
    {
        get { Track(); return _styleName; }
        set => SetField(ref _styleName, value ?? "");
    }

    [Qml]
    public bool Kerning
    {
        get { Track(); return _kerning; }
        set => SetField(ref _kerning, value);
    }

    [Qml]
    public bool PreferShaping
    {
        get { Track(); return _preferShaping; }
        set => SetField(ref _preferShaping, value);
    }

    [Qml]
    public object? Features { get; set; }

    [Qml]
    public object? VariableAxes { get; set; }

    public FontSpec Spec
    {
        get
        {
            using (DependencyTracker.Suspend())
                return new FontSpec(Family, PixelSize, _weight, _italic, _underline, _strikeout, _letterSpacing, _capitalization);
        }
    }

    /// <summary>A font of its own, not an item's.</summary>
    public FontGroup(string family, double pixelSize, int weight)
        : this(null)
    {
        _family = family;
        _pixelSize = pixelSize;
        _weight = weight;
    }

    /// <summary>Takes every value of another font, as assigning a whole font does.</summary>
    public void CopyFrom(FontGroup other)
    {
        if (ReferenceEquals(other, this))
            return;
        using (DependencyTracker.Suspend())
        {
            Family = other._family ?? "";
            if (other._pointSize > 0)
                PointSize = other._pointSize;
            else
                PixelSize = other.PixelSize;
            Weight = other._weight;
            Italic = other._italic;
            Underline = other._underline;
            Strikeout = other._strikeout;
            LetterSpacing = other._letterSpacing;
            WordSpacing = other._wordSpacing;
            Capitalization = other._capitalization;
        }
    }

    /// <summary>Copies a font value assigned whole: <c>font: Qt.font({ pixelSize: 12 })</c>.</summary>
    public void Assign(object? value)
    {
        if (ValueConverter.Unwrap(value) is not Jint.Native.JsValue js || !js.IsObject())
            return;
        var obj = js.AsObject();
        foreach (var key in obj.GetOwnPropertyKeys())
        {
            var name = key.ToString();
            if (HasProperty(name))
                WriteProperty(name, obj.Get(key));
        }
    }
}

/// <summary>Finds typefaces by family and style.</summary>
public static class FontCache
{
    private static readonly ConcurrentDictionary<(string, int, bool), SKTypeface> Typefaces = new();

    public static SKTypeface Get(string family, int weight, bool italic) =>
        Typefaces.GetOrAdd((family, weight, italic), static key =>
        {
            var style = new SKFontStyle(key.Item2, (int)SKFontStyleWidth.Normal, key.Item3 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            foreach (var name in Candidates(key.Item1))
            {
                var tf = SKFontManager.Default.MatchFamily(name, style);
                if (tf is not null)
                    return tf;
            }
            return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
        });

    /// <summary>A family name, then the names Windows ships when that one is missing.</summary>
    private static IEnumerable<string> Candidates(string family)
    {
        foreach (var part in family.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return part.Trim('"', '\'');
        if (family.Equals("monospace", StringComparison.OrdinalIgnoreCase))
            yield return "Cascadia Mono";
        if (family.Equals("sans-serif", StringComparison.OrdinalIgnoreCase) || family.Equals("sans", StringComparison.OrdinalIgnoreCase))
            yield return "Segoe UI Variable";
        if (family.Equals("serif", StringComparison.OrdinalIgnoreCase))
            yield return "Cambria";
        yield return "Segoe UI Variable";
        yield return "Segoe UI";
    }
}
