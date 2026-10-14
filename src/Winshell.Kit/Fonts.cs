using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Winshell.Kit;

/// <summary>The faces the Kit draws with.</summary>
public static class Fonts
{
    private const string TextStack = "Segoe UI Variable Text, Segoe UI Variable, Segoe UI";
    private const string DisplayStack = "Segoe UI Variable Display, Segoe UI Variable, Segoe UI";
    private const string MonoStack = "Cascadia Mono, Cascadia Code, Consolas, Courier New";
    private const string SmallStack = "Segoe UI Variable Small, Segoe UI Variable Text, Segoe UI";

    /// <summary>
    /// The Font in use, or empty while none is set. A Font is a monospace face that <see cref="Text"/> and <see cref="Display"/> put first,
    /// so every glyph it lacks falls back to Segoe UI Variable. Icons and emoji keep their own faces.
    /// </summary>
    public static string Font { get; private set; } = "";

    /// <summary>
    /// Segoe UI Variable Text, the face of a panel's rows, or the Font when one is set. <c>PanelTextStyle</c> sets it on a line of text.
    /// A box to type in takes it from here, or it draws in the face Windows gives a control.
    /// </summary>
    public static FontFamily Text => TextFace ??= new(Stack(Font, TextStack));

    /// <summary>Segoe UI Variable Display, for headings and large figures, or the Font when one is set.</summary>
    public static FontFamily Display => DisplayFace ??= new(Stack(Font, DisplayStack));

    /// <summary>Segoe Fluent Icons, for glyphs.</summary>
    public static FontFamily Icons => IconsFace ??= new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Segoe UI Emoji, for an emoji drawn as a picture.</summary>
    public static FontFamily Emoji => EmojiFace ??= new("Segoe UI Emoji, Segoe UI Symbol");

    /// <summary>
    /// A monospace face Windows ships, for text drawn in cells, such as About's art. Every cell is one width,
    /// and the block and box characters meet. The Font takes this place where the Host has one set.
    /// </summary>
    public static FontFamily Mono => MonoFace ??= new(Stack(Font, MonoStack));

    /// <summary>
    /// Draws later controls in <paramref name="font"/>: the Font from <c>settings.json</c>, or empty for Segoe UI Variable.
    /// A control already on screen keeps its face until the bar mounts it again.
    /// </summary>
    /// <param name="font">The Font, or empty.</param>
    public static void Use(string? font)
    {
        font = string.IsNullOrWhiteSpace(font) ? "" : font.Trim();
        if (font == Font)
        {
            return;
        }

        Font = font;
        MonoFace = null;
        TextFace = null;
        DisplayFace = null;
        Restyle();
    }

    // The app's three text styles carry the face as a setter, and a style a control has used cannot be changed. So each is made again from
    // the one XAML declared, with its face swapped, and put in the app's resources where Paint.Style finds it.
    private static void Restyle()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        Originals ??= new()
        {
            ["BarTextStyle"] = (Style)app.Resources["BarTextStyle"],
            ["PanelTextStyle"] = (Style)app.Resources["PanelTextStyle"],
            ["BarButtonStyle"] = (Style)app.Resources["BarButtonStyle"],
        };
        var bar = Font.Length == 0 ? Originals["BarTextStyle"] : Remade(Originals["BarTextStyle"], Stack(Font, SmallStack), Originals["BarTextStyle"].BasedOn);
        app.Resources["BarTextStyle"] = bar;
        app.Resources["PanelTextStyle"] = Font.Length == 0 ? Originals["PanelTextStyle"] : Remade(Originals["PanelTextStyle"], Stack(Font, TextStack), bar);
        app.Resources["BarButtonStyle"] = Font.Length == 0 ? Originals["BarButtonStyle"] : Remade(Originals["BarButtonStyle"], Stack(Font, TextStack), Originals["BarButtonStyle"].BasedOn);
        Paint.ForgetStyles();
    }

    // The Font first, then the fallback, which draws any glyph the Font lacks. No Font keeps the fallback alone.
    private static string Stack(string font, string fallback) =>
        string.IsNullOrWhiteSpace(font) ? fallback : font.Trim() + ", " + fallback;

    private static Style Remade(Style original, string stack, Style? basedOn)
    {
        var style = new Style(original.TargetType) { BasedOn = basedOn };
        foreach (var setter in original.Setters.OfType<Setter>())
        {
            var face = setter.Property == TextBlock.FontFamilyProperty || setter.Property == Control.FontFamilyProperty;
            style.Setters.Add(new Setter(setter.Property, face ? new FontFamily(stack) : setter.Value));
        }

        return style;
    }

    // Made on first use, on the one thread that draws, and shared. Every glyph and title in every row reads one of these.
    private static FontFamily? MonoFace;
    private static FontFamily? TextFace;
    private static FontFamily? DisplayFace;
    private static FontFamily? IconsFace;
    private static FontFamily? EmojiFace;
    private static Dictionary<string, Style>? Originals;
}
