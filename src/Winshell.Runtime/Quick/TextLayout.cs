using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>A piece of text in one style.</summary>
public sealed record TextSpan(string Text, FontSpec Font, QColor? Color = null, string? Href = null);

/// <summary>A run of text DirectWrite resolved to one font face, including its optical size in points.</summary>
public readonly record struct FontRun(string Family, string Text, float OpticalSize);

/// <summary>Text broken into lines and shaped into glyphs, ready to measure and draw.</summary>
public sealed partial class TextLayout : IDisposable
{
    public enum Wrap
    {
        NoWrap = 0,
        WordWrap = 1,
        WrapAnywhere = 3,
        WrapAtWordBoundaryOrAnywhere = 4,
    }

    public enum Elide
    {
        Left = 0,
        Right = 1,
        Middle = 2,
        None = 3,
    }

    public sealed class Run
    {
        public required SKTypeface Typeface { get; init; }

        public required float Size { get; init; }

        public required ushort[] Glyphs { get; init; }

        public required SKPoint[] Positions { get; init; }

        public required float Width { get; init; }

        public required string Text { get; init; }

        public required TextSpan Span { get; init; }

        public float X { get; set; }
    }

    public sealed class Line
    {
        public List<Run> Runs { get; } = [];

        public double Width { get; set; }

        public double Ascent { get; set; }

        public double Descent { get; set; }

        public double Y { get; set; }

        public double Height { get; set; }
    }

    public List<Line> Lines { get; } = [];

    /// <summary>The width of the widest line.</summary>
    public double Width { get; private set; }

    public double Height { get; private set; }

    public bool Truncated { get; private set; }

    private DirectWriteLayout? _native;

    /// <summary>The font faces DirectWrite used, in draw order.</summary>
    public IReadOnlyList<FontRun> FontRuns => _native?.FontRuns ?? [];

    /// <summary>How many times drawing made DirectWrite lay the text out again.</summary>
    internal int Relayouts => _native?.Relayouts ?? 0;

    public static TextLayout Build(
        IReadOnlyList<TextSpan> spans,
        double maxWidth,
        Wrap wrap,
        Elide elide,
        int maxLines,
        double lineHeight,
        bool lineHeightIsProportional)
    {
        var native = DirectWriteLayout.Create(spans, maxWidth, wrap, elide, maxLines, lineHeight, lineHeightIsProportional);
        var layout = new TextLayout { _native = native };
        layout.Width = native.Width;
        layout.Height = native.Height;
        layout.Truncated = native.Truncated;
        layout.Lines.AddRange(native.Lines);
        return layout;
    }

    /// <summary>The horizontal offset of a caret at a UTF-16 index, in DIPs.</summary>
    public double CaretX(int position) => _native?.CaretX(position) ?? 0;

    /// <summary>The UTF-16 index closest to a point in the layout, in DIPs.</summary>
    public int PositionAt(double x, double y = 0) => _native?.PositionAt(x, y) ?? 0;

    /// <summary>Underlines a range. Used for the IME preedit, which is not a separate text style.</summary>
    public void UnderlineRange(int start, int length) => _native?.UnderlineRange(start, length);

    /// <summary>Measures one string. Callers that only need a width use this.</summary>
    public static List<Run> Shape(string text, TextSpan span)
    {
        if (text.Length == 0)
            return [];
        var font = span.Font with { Capitalization = 0 };
        using var layout = DirectWriteLayout.Create([new TextSpan(text, font)], double.NaN, Wrap.NoWrap, Elide.None, 0, 1, true);
        var size = font.PixelSize;
        if (double.IsNaN(size) || size < 1)
            size = FontSpec.DefaultPixelSize;
        return
        [
            new Run
            {
                Typeface = FontCache.Get(font.Family, font.Weight, font.Italic),
                Size = (float)size,
                Glyphs = [],
                Positions = [],
                Width = (float)layout.Width,
                Text = text,
                Span = span,
            },
        ];
    }

    /// <summary>
    /// Draws the lines in a box of the given width, aligned as asked.
    /// <paramref name="decorate"/> is kept so existing callers compile; outline, raised, and sunken are drawn
    /// by DirectWrite from <paramref name="textStyle"/>.
    /// </summary>
    public void Draw(SKCanvas canvas, double boxWidth, int horizontalAlignment, QColor color, double offsetY, bool antialias, Action<SKCanvas, SKTextBlob, SKPaint>? decorate = null, PaintContext? paint = null, int textStyle = 0, QColor? styleColor = null)
    {
        _ = decorate;
        _native?.Draw(canvas, boxWidth, horizontalAlignment, color, offsetY, antialias, paint, textStyle, styleColor);
    }

    public void Dispose()
    {
        DisposeNative();
        GC.SuppressFinalize(this);
    }

    private void DisposeNative()
    {
        var native = Interlocked.Exchange(ref _native, null);
        native?.Dispose();
    }

    ~TextLayout() => DisposeNative();

    /// <summary>Whether text should be read as rich text, as Qt's AutoText decides.</summary>
    public static bool MightBeRichText(string text)
    {
        var lt = text.IndexOf('<');
        if (lt < 0)
            return false;
        return TagPattern().IsMatch(text);
    }

    [GeneratedRegex(@"<\s*/?\s*[a-zA-Z][a-zA-Z0-9]*(\s[^>]*)?/?\s*>")]
    private static partial Regex TagPattern();

    /// <summary>Turns the HTML subset Qt's StyledText and RichText accept into styled spans.</summary>
    public static List<TextSpan> ParseRichText(string html, FontSpec font, QColor? linkColor)
    {
        var spans = new List<TextSpan>();
        var stack = new Stack<(FontSpec Font, QColor? Color, string? Href)>();
        var state = (Font: font, Color: (QColor?)null, Href: (string?)null);
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length == 0)
                return;
            spans.Add(new TextSpan(WebUtility.HtmlDecode(sb.ToString()), state.Font, state.Color, state.Href));
            sb.Clear();
        }
        var i = 0;
        while (i < html.Length)
        {
            var c = html[i];
            if (c == '<')
            {
                var end = html.IndexOf('>', i);
                if (end < 0)
                {
                    sb.Append(html, i, html.Length - i);
                    break;
                }
                var tag = html[(i + 1)..end].Trim();
                i = end + 1;
                var closing = tag.StartsWith('/');
                var selfClosing = tag.EndsWith('/');
                var body = tag.Trim('/').Trim();
                var name = body.Split(' ', 2)[0].ToLowerInvariant();
                var attrs = body.Length > name.Length ? body[name.Length..] : "";
                if (name is "br")
                {
                    sb.Append('\n');
                    continue;
                }
                if (name is "p" or "div" && closing)
                {
                    sb.Append('\n');
                }
                if (closing)
                {
                    Flush();
                    if (stack.Count > 0)
                        state = stack.Pop();
                    continue;
                }
                if (selfClosing || name is "img" or "hr")
                    continue;
                Flush();
                stack.Push(state);
                var f = state.Font;
                switch (name)
                {
                    case "b" or "strong":
                        f = f with { Weight = 700 };
                        break;
                    case "i" or "em":
                        f = f with { Italic = true };
                        break;
                    case "u":
                        f = f with { Underline = true };
                        break;
                    case "s" or "strike" or "del":
                        f = f with { Strikeout = true };
                        break;
                    case "small":
                        f = f with { PixelSize = f.PixelSize * 0.83 };
                        break;
                    case "big":
                        f = f with { PixelSize = f.PixelSize * 1.2 };
                        break;
                    case "code" or "tt" or "pre" or "kbd":
                        f = f with { Family = "Cascadia Mono" };
                        break;
                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        f = f with { Weight = 700, PixelSize = f.PixelSize * (name[1] switch { '1' => 2.0, '2' => 1.5, '3' => 1.17, '4' => 1.0, '5' => 0.83, _ => 0.67 }) };
                        break;
                    case "a":
                        state.Href = Attribute(attrs, "href");
                        state.Color = linkColor ?? QColor.Parse("#0078d4");
                        f = f with { Underline = true };
                        break;
                }
                if (Attribute(attrs, "color") is { } colorText && QColor.TryParse(colorText, out var col))
                    state.Color = col;
                if (Attribute(attrs, "style") is { } style)
                {
                    foreach (var decl in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = decl.Split(':', 2);
                        if (kv.Length != 2)
                            continue;
                        var key = kv[0].Trim().ToLowerInvariant();
                        var value = kv[1].Trim();
                        if (key == "color" && QColor.TryParse(value, out var sc))
                            state.Color = sc;
                        else if (key == "font-weight")
                            f = f with { Weight = value == "bold" ? 700 : int.TryParse(value, out var w) ? w : f.Weight };
                        else if (key == "font-style" && value == "italic")
                            f = f with { Italic = true };
                        else if (key == "font-size" && value.EndsWith("px", StringComparison.Ordinal) && double.TryParse(value[..^2], CultureInfo.InvariantCulture, out var px))
                            f = f with { PixelSize = px };
                        else if (key == "font-size" && value.EndsWith("pt", StringComparison.Ordinal) && double.TryParse(value[..^2], CultureInfo.InvariantCulture, out var pt))
                            f = f with { PixelSize = pt * 96 / 72 };
                        else if (key == "font-family")
                            f = f with { Family = value.Trim('\'', '"') };
                        else if (key == "text-decoration" && value.Contains("underline", StringComparison.Ordinal))
                            f = f with { Underline = true };
                    }
                }
                if (name == "font" && Attribute(attrs, "size") is { } sizeText && int.TryParse(sizeText.TrimStart('+'), out var size))
                    f = f with { PixelSize = font.PixelSize * (sizeText.StartsWith('+') ? 1 + size * 0.2 : size / 3.0) };
                if (name == "font" && Attribute(attrs, "face") is { } face)
                    f = f with { Family = face };
                state.Font = f;
                continue;
            }
            if (c == '\n' || c == '\r')
            {
                // Newlines in rich text are spaces.
                sb.Append(' ');
                i++;
                continue;
            }
            sb.Append(c);
            i++;
        }
        Flush();
        return spans;
    }

    private static string? Attribute(string attrs, string name)
    {
        var m = Regex.Match(attrs, name + @"\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;
        return m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
    }
}

/// <summary>
/// One line of plain text kept between paints. It is built again only when its text, font, width or elide mode
/// changes, and the old layout is disposed then.
/// </summary>
internal sealed class LineLayoutCache : IDisposable
{
    private TextLayout? _layout;
    private (string Text, FontSpec Font, double Width, TextLayout.Elide Elide) _key;

    /// <summary>How many layouts this cache has built.</summary>
    internal int Builds { get; private set; }

    /// <summary>The layout for these inputs. A NaN width doesn't wrap or elide. Callers don't dispose it.</summary>
    public TextLayout Get(string text, FontSpec font, double width, TextLayout.Elide elide)
    {
        var key = (text, font, width, elide);
        // Equals, not ==, so a NaN width matches itself.
        if (_layout is not null && _key.Equals(key))
            return _layout;
        _layout?.Dispose();
        _layout = TextLayout.Build([new TextSpan(text, font)], width, TextLayout.Wrap.NoWrap, elide, elide == TextLayout.Elide.None ? 0 : 1, 1, true);
        _key = key;
        Builds++;
        return _layout;
    }

    public void Dispose()
    {
        _layout?.Dispose();
        _layout = null;
    }
}
