using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Tests that change <see cref="Theme.Current"/>, which every test in the process reads.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThemeCollection
{
    public const string Name = "Theme";
}

[Collection(ThemeCollection.Name)]
public class ThemeTests
{
    private static readonly AccentColors Teal = new(
        QColor.Parse("#00b294"), QColor.Parse("#00c8a8"), QColor.Parse("#38e0c0"), QColor.Parse("#80f0dc"),
        QColor.Parse("#008a74"), QColor.Parse("#006a59"), QColor.Parse("#00483c"));

    private static readonly SystemColors NightSky = new(
        Window: QColor.Parse("#000000"), WindowText: QColor.Parse("#ffffff"), Highlight: QColor.Parse("#8ee3f0"),
        HighlightText: QColor.Parse("#263b50"), Hotlight: QColor.Parse("#ffff00"), GrayText: QColor.Parse("#a6a6a6"),
        ButtonFace: QColor.Parse("#202020"), ButtonText: QColor.Parse("#ffffff"));

    [Fact]
    public void Tokens_have_the_winui_values_in_light_and_dark()
    {
        var light = Theme.Default(false);
        var dark = Theme.Default(true);

        Assert.True(light.AnimationsEnabled);
        Assert.True(dark.AnimationsEnabled);
        Assert.Equal(QColor.Parse("#e4000000"), light.TextFillColorPrimary);
        Assert.Equal(QColor.Parse("#ffffff"), dark.TextFillColorPrimary);
        Assert.Equal(QColor.Parse("#b3ffffff"), light.ControlFillColorDefault);
        Assert.Equal(QColor.Parse("#0fffffff"), dark.ControlFillColorDefault);
        Assert.Equal(QColor.Parse("#29000000"), light.ControlStrokeColorSecondary);
        Assert.Equal(QColor.Parse("#18ffffff"), dark.ControlStrokeColorSecondary);
        Assert.Equal(QColor.Parse("#f3f3f3"), light.SolidBackgroundFillColorBase);
        Assert.Equal(QColor.Parse("#202020"), dark.SolidBackgroundFillColorBase);
        Assert.Equal(QColor.Parse("#33000000"), dark.SurfaceStrokeColorFlyout);
        Assert.Equal(QColor.Parse("#c42b1c"), light.SystemFillColorCritical);
        Assert.Equal(QColor.Parse("#442726"), dark.SystemFillColorCriticalBackground);
    }

    [Fact]
    public void Accent_tokens_are_made_from_the_accent_shades_winui_uses()
    {
        var light = Theme.Default(false) with { Accents = Teal };
        var dark = Theme.Default(true) with { Accents = Teal };

        Assert.Equal(Teal.Dark1, light.AccentFillColorDefault);
        Assert.Equal(Teal.Light2, dark.AccentFillColorDefault);
        Assert.Equal(Teal.Light2.WithAlpha(0.9), dark.AccentFillColorSecondary);
        Assert.Equal(Teal.Light2.WithAlpha(0.8), dark.AccentFillColorTertiary);
        // Accent text is a shade lighter than the accent fill in dark, and darker in light.
        Assert.Equal(Teal.Light3, dark.AccentTextFillColorPrimary);
        Assert.Equal(Teal.Dark2, light.AccentTextFillColorPrimary);
        Assert.Equal(Teal.Accent, light.AccentFillColorSelectedTextBackground);
        Assert.Equal(Teal.Accent, dark.SystemAccentColor);
    }

    [Fact]
    public void A_contrast_theme_maps_every_token_to_a_system_color()
    {
        var theme = Theme.Default(true) with { Accents = Teal, System = NightSky, IsHighContrast = true };

        Assert.Equal(NightSky.WindowText, theme.TextFillColorPrimary);
        Assert.Equal(NightSky.GrayText, theme.TextFillColorDisabled);
        Assert.Equal(NightSky.ButtonFace, theme.ControlFillColorDefault);
        Assert.Equal(NightSky.ButtonText, theme.ControlStrokeColorDefault);
        Assert.Equal(NightSky.Window, theme.SolidBackgroundFillColorBase);
        Assert.Equal(NightSky.Window, theme.AccentFillColorDefault);
        Assert.Equal(NightSky.WindowText, theme.FocusStrokeColorOuter);
        Assert.Equal(NightSky.Window, theme.FocusStrokeColorInner);
        Assert.Equal(QColor.Transparent, theme.SubtleFillColorTransparent);
        Assert.Equal(0, theme.FlyoutShadow.Color.A);

        var colors = typeof(Theme).GetProperties().Where(p => p.PropertyType == typeof(QColor)).Select(p => (QColor)p.GetValue(theme)!);
        var system = new[] { NightSky.Window, NightSky.WindowText, NightSky.Highlight, NightSky.HighlightText, NightSky.Hotlight, NightSky.GrayText, NightSky.ButtonFace, NightSky.ButtonText };
        Assert.All(colors.Where(c => c.A > 0), c => Assert.Contains(c with { A = 1 }, system.Concat([Teal.Accent, Teal.Light1, Teal.Light2, Teal.Light3, Teal.Dark1, Teal.Dark2, Teal.Dark3])));
    }

    [Fact]
    public void Shadows_are_darker_in_dark()
    {
        Assert.Equal(16, Theme.Default(false).FlyoutShadow.Blur);
        Assert.Equal(8, Theme.Default(false).FlyoutShadow.OffsetY);
        Assert.True(Theme.Default(true).FlyoutShadow.Color.A > Theme.Default(false).FlyoutShadow.Color.A);
    }

    [Fact]
    public void Text_and_palette_follow_a_theme_change()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(false);
            var root = TestShell.Run("""
                import QtQuick
                Item {
                  property color text: label.color
                  property color window: label.palette.window
                  Text { id: label; text: "Hi" }
                }
                """, out var shell);
            using var _ = shell;
            Assert.Equal(QColor.Parse("#e4000000"), root.ReadProperty("text"));
            Assert.Equal(QColor.Parse("#f3f3f3"), root.ReadProperty("window"));

            Theme.Current = Theme.Default(true);
            shell.Loop.Process();
            Assert.Equal(QColor.Parse("#ffffff"), root.ReadProperty("text"));
            Assert.Equal(QColor.Parse("#202020"), root.ReadProperty("window"));

            Theme.Current = Theme.Default(true) with { System = NightSky, IsHighContrast = true };
            shell.Loop.Process();
            Assert.Equal(NightSky.WindowText, root.ReadProperty("text"));
            Assert.Equal(NightSky.Window, root.ReadProperty("window"));
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void Assigning_a_whole_font_copies_it()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property real size: copy.font.pixelSize
              property int weight: copy.font.weight
              property string family: copy.font.family
              Text { id: source; font.pixelSize: 28; font.weight: Font.DemiBold; font.family: "Cascadia Mono" }
              Text { id: copy; font: source.font }
            }
            """, out var shell);
        using var _ = shell;
        Assert.Equal(28.0, root.ReadProperty("size"));
        Assert.Equal(600, root.ReadProperty("weight"));
        Assert.Equal("Cascadia Mono", root.ReadProperty("family"));
    }

    [Fact]
    public void A_chosen_accent_keeps_its_hue_and_gets_shades_as_far_apart_as_Windows_makes_them()
    {
        var windows = AccentColors.Default;
        var made = AccentColors.From(windows.Accent);

        Assert.Equal(windows.Accent, made.Accent);
        var shades = new[] { made.Dark3, made.Dark2, made.Dark1, made.Accent, made.Light1, made.Light2, made.Light3 };
        var expected = new[] { windows.Dark3, windows.Dark2, windows.Dark1, windows.Accent, windows.Light1, windows.Light2, windows.Light3 };
        for (var i = 0; i < shades.Length; i++)
        {
            Assert.Equal(expected[i].HslLightness, shades[i].HslLightness, 0.01);
            Assert.Equal(made.Accent.HslHue, shades[i].HslHue, 0.001);
            if (i > 0)
                Assert.True(shades[i].HslLightness > shades[i - 1].HslLightness);
        }

        // A light accent still has room for three lighter shades, and a dark one for three darker shades.
        foreach (var accent in new[] { QColor.Parse("#ffb900"), QColor.Parse("#4c4a48") })
        {
            var a = AccentColors.From(accent);
            Assert.True(a.Light3.HslLightness < 1 && a.Light3.HslLightness > a.Light2.HslLightness);
            Assert.True(a.Dark3.HslLightness > 0 && a.Dark3.HslLightness < a.Dark2.HslLightness);
        }
    }

    [Fact]
    public void The_shell_choice_applies_on_top_of_what_Windows_reports_until_its_owner_takes_it_back()
    {
        var saved = Theme.Current;
        object first = new(), second = new();
        var changes = 0;
        void Count() => changes++;
        Theme.Changed += Count;
        try
        {
            Theme.Current = Theme.Default(false);
            Theme.Choose(first, new ThemeChoice(Dark: true));
            Assert.True(Theme.Current.IsDark);
            Assert.False(Theme.Reported.IsDark);
            Assert.Equal(1, changes);

            // Windows changes underneath; the choice stays on top.
            Theme.Current = Theme.Default(false) with { Accents = Teal, IsHighContrast = true };
            Assert.True(Theme.Current.IsDark);
            Assert.Equal(Teal, Theme.Current.Accents);
            Assert.True(Theme.Current.IsHighContrast);

            // A choice that draws the same theme is still a change of the choice.
            Theme.Current = Theme.Default(true);
            changes = 0;
            Theme.Choose(first, ThemeChoice.None);
            Assert.Equal(1, changes);
            Theme.Choose(first, new ThemeChoice(Dark: true));

            // A reload's new generation chooses before the old one goes away; the old one cannot take it back.
            Theme.Choose(second, new ThemeChoice(Accent: QColor.Parse("#e81123")));
            Theme.Release(first);
            Assert.Equal(QColor.Parse("#e81123"), Theme.Current.Accents.Accent);
            Assert.True(Theme.Current.IsDark);

            Theme.Current = Theme.Default(false);
            Theme.Release(second);
            Assert.Equal(Theme.Default(false), Theme.Current);
            Assert.Equal(ThemeChoice.None, Theme.Choice);
        }
        finally
        {
            Theme.Changed -= Count;
            Theme.Release(first);
            Theme.Release(second);
            Theme.Current = saved;
        }
    }
}
