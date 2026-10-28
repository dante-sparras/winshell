using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>A piece of text in one style.</summary>
public sealed record TextSpan(string Text, FontSpec Font, QColor? Color = null, string? Href = null);

/// <summary>Text broken into lines and shaped into glyphs, ready to measure and draw.</summary>
public sealed partial class TextLayout
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

    private static readonly ConcurrentDictionary<(string, FontSpec), List<Run>> ShapeCache = new();

    public static TextLayout Build(
        IReadOnlyList<TextSpan> spans,
        double maxWidth,
        Wrap wrap,
        Elide elide,
        int maxLines,
        double lineHeight,
        bool lineHeightIsProportional)
    {
        var layout = new TextLayout();
        var wrapping = wrap != Wrap.NoWrap && maxWidth > 0 && !double.IsNaN(maxWidth);
        var current = new Line();
        var x = 0.0;
        var lines = layout.Lines;
        var primary = spans.Count > 0 ? spans[0].Font : FontSpec.Default;
        void Finish()
        {
            // Trailing spaces do not count toward the width of a wrapped line.
            current.Width = x;
            if (current.Runs.Count > 0 && wrapping)
            {
                var last = current.Runs[^1];
                if (last.Text.Length > 0 && char.IsWhiteSpace(last.Text[^1]) && last.Text.Trim().Length == 0)
                    current.Width -= last.Width;
            }
            Metrics(current, primary);
            lines.Add(current);
            current = new Line();
            x = 0;
        }

        foreach (var span in spans)
        {
            var text = Capitalize(span.Text, span.Font.Capitalization);
            var paragraphs = text.Replace("\r\n", "\n").Split('\n');
            for (var p = 0; p < paragraphs.Length; p++)
            {
                if (p > 0)
                    Finish();
                foreach (var token in Tokens(paragraphs[p], wrapping))
                {
                    var runs = Shape(token, span);
                    var width = runs.Sum(r => r.Width);
                    if (wrapping && x > 0 && x + width > maxWidth && token.Trim().Length > 0)
                        Finish();
                    if (wrapping && width > maxWidth && wrap is Wrap.WrapAnywhere or Wrap.WrapAtWordBoundaryOrAnywhere)
                    {
                        // A word longer than the line breaks between characters.
                        var remaining = token;
                        while (remaining.Length > 0)
                        {
                            var fit = FitPrefix(remaining, span, maxWidth - x);
                            if (fit == 0)
                            {
                                if (x > 0)
                                {
                                    Finish();
                                    continue;
                                }
                                fit = 1;
                            }
                            var piece = remaining[..fit];
                            foreach (var r in Shape(piece, span))
                                Place(current, r, ref x);
                            remaining = remaining[fit..];
                            if (remaining.Length > 0)
                                Finish();
                        }
                        continue;
                    }
                    foreach (var r in runs)
                        Place(current, r, ref x);
                }
            }
        }
        Finish();

        if (maxLines > 0 && lines.Count > maxLines)
        {
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            layout.Truncated = true;
            if (elide != Elide.None && lines.Count > 0)
                ElideLine(lines[^1], maxWidth > 0 ? maxWidth : lines[^1].Width, Elide.Right, force: true);
        }
        if (elide != Elide.None && maxWidth > 0 && !double.IsNaN(maxWidth))
        {
            foreach (var line in lines)
            {
                if (line.Width > maxWidth + 0.5)
                {
                    ElideLine(line, maxWidth, elide, force: false);
                    layout.Truncated = true;
                }
            }
        }

        var y = 0.0;
        foreach (var line in lines)
        {
            var natural = line.Ascent + line.Descent;
            line.Height = lineHeightIsProportional ? natural * lineHeight : lineHeight > 0 ? lineHeight : natural;
            line.Y = y;
            y += line.Height;
        }
        layout.Width = lines.Count == 0 ? 0 : lines.Max(l => l.Width);
        layout.Height = y;
        return layout;
    }

    private static void Place(Line line, Run run, ref double x)
    {
        var placed = new Run
        {
            Typeface = run.Typeface,
            Size = run.Size,
            Glyphs = run.Glyphs,
            Positions = run.Positions,
            Width = run.Width,
            Text = run.Text,
            Span = run.Span,
            X = (float)x,
        };
        line.Runs.Add(placed);
        x += run.Width;
    }

    private static void Metrics(Line line, FontSpec primary)
    {
        double ascent = 0, descent = 0;
        void Take(SKTypeface tf, float size)
        {
            using var font = new SKFont(tf, size);
            font.GetFontMetrics(out var m);
            ascent = Math.Max(ascent, -m.Ascent);
            descent = Math.Max(descent, m.Descent);
        }
        if (line.Runs.Count == 0)
            Take(FontCache.Get(primary.Family, primary.Weight, primary.Italic), (float)primary.PixelSize);
        foreach (var run in line.Runs)
            Take(run.Typeface, run.Size);
        line.Ascent = Math.Ceiling(ascent);
        line.Descent = Math.Ceiling(descent);
    }

    private static IEnumerable<string> Tokens(string text, bool wrapping)
    {
        if (!wrapping)
        {
            yield return text;
            yield break;
        }
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' || text[i] == '\t')
            {
                // A word with the spaces after it.
                var end = i;
                while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
                    end++;
                yield return text[start..end];
                start = end;
                i = end - 1;
            }
        }
        if (start < text.Length)
            yield return text[start..];
    }

    private static int FitPrefix(string text, TextSpan span, double width)
    {
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Shape(text[..mid], span).Sum(r => r.Width) <= width)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    private static void ElideLine(Line line, double maxWidth, Elide mode, bool force)
    {
        var text = string.Concat(line.Runs.Select(r => r.Text)).TrimEnd();
        var span = line.Runs.Count > 0 ? line.Runs[0].Span : new TextSpan("", FontSpec.Default);
        const string ellipsis = "…";
        string candidate = text;
        if (force || Shape(text, span).Sum(r => r.Width) > maxWidth)
        {
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                var trial = Cut(text, mid, mode) ;
                if (Shape(trial, span).Sum(r => r.Width) <= maxWidth)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            candidate = Cut(text, lo, mode);
        }
        line.Runs.Clear();
        var x = 0.0;
        foreach (var r in Shape(candidate, span))
            Place(line, r, ref x);
        line.Width = x;

        static string Cut(string text, int keep, Elide mode) => mode switch
        {
            Elide.Left => ellipsis + text[(text.Length - keep)..],
            Elide.Middle => text[..((keep + 1) / 2)] + ellipsis + text[(text.Length - keep / 2)..],
            _ => text[..keep].TrimEnd() + ellipsis,
        };
    }

    private static string Capitalize(string text, int mode) => mode switch
    {
        1 => text.ToUpperInvariant(),
        2 => text.ToLowerInvariant(),
        4 => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text),
        _ => text,
    };

    /// <summary>Shapes text in one style into runs, one per typeface where characters need a fallback.</summary>
    public static List<Run> Shape(string text, TextSpan span)
    {
        if (text.Length == 0)
            return [];
        var font = span.Font;
        var cached = ShapeCache.GetOrAdd((text, font), static key => ShapeUncached(key.Item1, key.Item2));
        if (ShapeCache.Count > 20000)
            ShapeCache.Clear();
        return cached.Select(r => new Run
        {
            Typeface = r.Typeface,
            Size = r.Size,
            Glyphs = r.Glyphs,
            Positions = r.Positions,
            Width = r.Width,
            Text = r.Text,
            Span = span,
        }).ToList();
    }

    private static List<Run> ShapeUncached(string text, FontSpec font)
    {
        var primary = FontCache.Get(font.Family, font.Weight, font.Italic);
        var runs = new List<Run>();
        var size = (float)font.PixelSize;
        var pieces = new List<(SKTypeface Typeface, string Text)>();
        var sb = new StringBuilder();
        SKTypeface? currentFace = null;
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var element = (string)e.Current;
            var cp = char.ConvertToUtf32(element, 0);
            SKTypeface face;
            if (cp is '\t' or ' ' || FontCache.Contains(primary, cp))
                face = primary;
            else
                face = FontCache.Fallback(cp, font.Weight, font.Italic) ?? primary;
            if (currentFace is not null && face != currentFace)
            {
                pieces.Add((currentFace, sb.ToString()));
                sb.Clear();
            }
            currentFace = face;
            sb.Append(element);
        }
        if (currentFace is not null && sb.Length > 0)
            pieces.Add((currentFace, sb.ToString()));

        foreach (var (face, piece) in pieces)
        {
            using var skFont = new SKFont(face, size) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
            var shaper = FontCache.Shaper(face);
            var result = shaper.Shape(piece.Replace('\t', ' '), skFont);
            var glyphs = result.Codepoints.Select(c => (ushort)c).ToArray();
            var positions = result.Points;
            var width = result.Width;
            if (font.LetterSpacing != 0 && glyphs.Length > 0)
            {
                var adjusted = new SKPoint[positions.Length];
                for (var i = 0; i < positions.Length; i++)
                    adjusted[i] = new SKPoint(positions[i].X + (float)(font.LetterSpacing * i), positions[i].Y);
                positions = adjusted;
                width += (float)(font.LetterSpacing * glyphs.Length);
            }
            runs.Add(new Run
            {
                Typeface = face,
                Size = size,
                Glyphs = glyphs,
                Positions = positions,
                Width = width,
                Text = piece,
                Span = new TextSpan(piece, font),
            });
        }
        return runs;
    }

    /// <summary>Draws the lines in a box of the given width, aligned as asked.</summary>
    public void Draw(SKCanvas canvas, double boxWidth, int horizontalAlignment, QColor color, double offsetY, bool antialias, Action<SKCanvas, SKTextBlob, SKPaint>? decorate = null)
    {
        foreach (var line in Lines)
        {
            var lineX = (horizontalAlignment & QtEnums.AlignRight) != 0 ? boxWidth - line.Width
                : (horizontalAlignment & QtEnums.AlignHCenter) != 0 ? (boxWidth - line.Width) / 2
                : 0;
            var baseline = offsetY + line.Y + (line.Height - (line.Ascent + line.Descent)) / 2 + line.Ascent;
            foreach (var run in line.Runs)
            {
                if (run.Glyphs.Length == 0)
                    continue;
                using var font = new SKFont(run.Typeface, run.Size) { Subpixel = true, Edging = antialias ? SKFontEdging.Antialias : SKFontEdging.Alias };
                using var builder = new SKTextBlobBuilder();
                var buffer = builder.AllocatePositionedRun(font, run.Glyphs.Length);
                run.Glyphs.CopyTo(buffer.Glyphs);
                var positions = buffer.Positions;
                for (var i = 0; i < run.Glyphs.Length; i++)
                    positions[i] = new SKPoint(run.Positions[i].X + run.X + (float)lineX, run.Positions[i].Y + (float)baseline);
                using var blob = builder.Build();
                if (blob is null)
                    continue;
                using var paint = new SKPaint { IsAntialias = antialias, Color = (run.Span.Color ?? color).ToSk() };
                decorate?.Invoke(canvas, blob, paint);
                canvas.DrawText(blob, 0, 0, paint);
                if (run.Span.Font.Underline || run.Span.Font.Strikeout)
                {
                    font.GetFontMetrics(out var m);
                    using var line2 = new SKPaint { Color = paint.Color, StrokeWidth = Math.Max(1, m.UnderlineThickness ?? 1), IsAntialias = antialias };
                    var x0 = run.X + (float)lineX;
                    if (run.Span.Font.Underline)
                    {
                        var uy = (float)baseline + (m.UnderlinePosition ?? 2);
                        canvas.DrawLine(x0, uy, x0 + run.Width, uy, line2);
                    }
                    if (run.Span.Font.Strikeout)
                    {
                        var sy = (float)baseline + (m.StrikeoutPosition ?? -run.Size / 3);
                        canvas.DrawLine(x0, sy, x0 + run.Width, sy, line2);
                    }
                }
            }
        }
    }

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
