using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Winshell.Core.Diagnostics;

namespace Winshell.Kit;

/// <summary>
/// Provider marks and meter colors for the agents widget. The marks are the SVGs in <c>Assets\Agents</c>.
/// A white mark has a dark twin for light surfaces, picked from the live foreground like Agent Usage Plus does.
/// </summary>
public static class AgentMarks
{
    // Marks whose artwork runs closer to the edge of its box than the rest are drawn a little smaller, so all read the same size.
    private static readonly Dictionary<string, (string File, bool Twin, double Scale)> Known = new(StringComparer.Ordinal)
    {
        ["claude"] = ("claude", false, 0.88),
        ["codex"] = ("codex", true, 0.8),
        ["fireworks"] = ("fireworks", false, 1),
        ["openrouter"] = ("openrouter", true, 1),
        ["deepseek"] = ("deepseek", false, 1),
        ["gemini"] = ("gemini", true, 1),
        ["cursor"] = ("cursor", true, 1),
        ["kimi"] = ("kimi", true, 1),
        ["xai"] = ("xai", true, 1),
        ["grok"] = ("grok", true, 1),
        ["grok-bot"] = ("grok-bot.png", false, 1),
        ["zai"] = ("zai", true, 1),
        ["devin"] = ("devin", true, 1),
        ["agy"] = ("agy", true, 1),
        ["opencode"] = ("opencode-go", true, 1),
        ["opencode-go"] = ("opencode-go", true, 1),
    };

    private static readonly Dictionary<string, byte[]?> Files = new(StringComparer.Ordinal);

    /// <summary>True when the bar draws dark text, so a white mark needs its dark twin.</summary>
    public static bool LightSurface => Paint.LightSurface;

    /// <summary>The folder of the provider marks. An extension that ships them points this at its own <c>Assets\Agents</c>.</summary>
    public static string Folder { get; set; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Agents");

    /// <summary>
    /// <paramref name="mark"/> at <paramref name="size"/> pixels square: the provider's SVG, or its initial in a circle when Winoma has no mark for it.
    /// </summary>
    /// <param name="mark">The provider's mark id.</param>
    /// <param name="size">Edge in device-independent pixels.</param>
    /// <returns>The element to place.</returns>
    public static FrameworkElement Element(string mark, double size)
    {
        if (Known.TryGetValue(mark, out var known) && Bytes(known.Twin && LightSurface ? LightFile(known.File) : known.File) is { } file)
        {
            var drawn = Math.Round(size * known.Scale);
            var image = new Image
            {
                Width = drawn,
                Height = drawn,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // Decoded once per mark and size. The bar draws its marks again whenever a figure changes,
            // and a fresh decode each time left the mark blank for a frame.
            var key = (file, (int)drawn);
            if (!Sources.TryGetValue(key, out var kept))
            {
                if (file.Length > 4 && file[0] == 0x89 && file[1] == 0x50)
                {
                    kept = Bitmap(file);
                }
                else
                {
                    var source = new SvgImageSource { RasterizePixelWidth = drawn * 2, RasterizePixelHeight = drawn * 2 };
                    // The stream must outlive the load, so it is not disposed here.
                    _ = source.SetSourceAsync(new MemoryStream(file, writable: false).AsRandomAccessStream());
                    kept = source;
                }

                Sources[key] = kept;
            }

            image.Source = kept;

            return new Grid
            {
                Width = size,
                Height = size,
                Children = { image },
            };
        }

        if (mark is "agents" or "")
        {
            return new FontIcon
            {
                Glyph = Glyphs.Named("agents"),
                FontFamily = Fonts.Icons,
                FontSize = size * 0.9,
                Width = size,
                Height = size,
                Foreground = Brush("BarForegroundBrush"),
            };
        }

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = Brush("BarSelectedBrush"),
            Child = new TextBlock
            {
                Text = mark[..1].ToUpperInvariant(),
                FontSize = Math.Max(9, size * 0.55),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush("BarForegroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>The fill for a meter in <paramref name="tone"/>: the accent, or Windows' success, caution, or critical color for the theme.</summary>
    /// <param name="tone">The tone.</param>
    /// <returns>The brush.</returns>
    public static Brush Fill(PanelTone tone) => Paint.Fill(tone);

    /// <summary>Text in <paramref name="tone"/>: muted for <see cref="PanelTone.Normal"/>, else the tone's color.</summary>
    /// <param name="tone">The tone.</param>
    /// <returns>The brush.</returns>
    public static Brush Ink(PanelTone tone) => Paint.Ink(tone);


    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static string LightFile(string file)
    {
        var dot = file.LastIndexOf('.');
        return dot < 0 ? file + "-light" : file[..dot] + "-light" + file[dot..];
    }

    // Keyed by the file's bytes, which Bytes hands out as one array per file, and the size drawn. Read on the bar's thread only.
    private static readonly Dictionary<(byte[] File, int Pixels), ImageSource> Sources = [];

    private static BitmapImage Bitmap(byte[] png)
    {
        var image = new BitmapImage();
        image.SetSource(new MemoryStream(png, writable: false).AsRandomAccessStream());
        return image;
    }

    private static byte[]? Bytes(string file)
    {
        if (Files.TryGetValue(file, out var cached))
        {
            return cached;
        }

        byte[]? bytes = null;
        var name = file.Contains('.') ? file : file + ".svg";
        var path = Path.Combine(Folder, name);
        try
        {
            if (File.Exists(path))
            {
                bytes = File.ReadAllBytes(path);
            }
        }
        catch (IOException ex)
        {
            Log.Warning("Plugin", "agent mark " + file + " unreadable", ex);
        }

        Files[file] = bytes;
        return bytes;
    }
}
