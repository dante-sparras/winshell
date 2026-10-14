using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Winshell.Kit;

/// <summary>
/// What a bar item draws for the face its plugin publishes (ADR 0045): a glyph or a picture, text, readouts, meters, chips, or the tray.
/// A plugin with a plain item publishes a <see cref="BarFace"/> and writes no view code. One that draws its own item
/// implements <see cref="IKitBar"/>, and this frame holds that drawing instead. The host keeps the pointer and the panel.
/// </summary>
public sealed class BarButton
{
    private const double TrayHit = 32;
    private const double TrayGap = 4;

    private readonly ContentControl _host;
    private readonly bool _vertical;
    private readonly BarEdge _edge;
    private readonly Action<WidgetGesture, int> _gesture;
    // Where each part sits in the body, left to right. A part is made by the first face that draws it, and takes its place by this order.
    private const int MenuRank = 0;
    private const int TrayRank = 1;
    private const int WeatherRank = 2;
    private const int IconRank = 3;
    private const int TextRank = 4;
    private const int ReadoutsRank = 5;
    private const int MetersRank = 6;
    private const int StripRank = 7;

    // No face has this mark, so the next face is drawn in full.
    private const string NoMark = "";

    // Most items draw a glyph and a line of text, so the other parts stay null until a face asks for them.
    private ChipStrip? _strip;
    private StackPanel? _pinnedIcons;
    private Border? _chevron;
    private Grid? _chevronPivot;
    private StackPanel? _tray;
    private TextBlock? _text;
    private TextMarquee? _marquee;
    private Grid? _mark;
    private FontIcon? _icon;
    private Image? _picture;
    private byte[]? _pictureBytes;
    private Grid? _menu;
    private Grid? _weather;
    private StackPanel? _body;
    private readonly List<int> _ranks = [];
    private StackPanel? _readouts;
    private readonly List<TextBlock> _readoutValues = [];
    private readonly List<string> _readoutShown = [];
    private IReadOnlyList<BarReadout> _shownReadouts = [];
    private StackPanel? _meters;
    private IReadOnlyList<BarMeter> _shownMeters = [];
    private string _meterMark = "";
    private string _drawnTray = "";
    private string _face = NoMark;
    private BarFace? _drawn;
    private string _name = "";
    private string _tooltip = "";
    private Thickness _padding;
    private PanelTone _tone;
    private bool _held;
    private bool _active;
    private bool _chevronPlaced;
    private bool _watching;

    /// <summary>Draws faces into <paramref name="host"/>, the bar slot the host made.</summary>
    /// <param name="host">The slot. Its content, padding, name, tooltip, and visibility belong to this frame.</param>
    /// <param name="vertical">True on a bar along the left or right edge.</param>
    /// <param name="edge">The bar's edge, which turns the tray's chevron.</param>
    /// <param name="gesture">Hears a click on a chip or a pinned tray icon, with its tag.</param>
    public BarButton(ContentControl host, bool vertical, BarEdge edge, Action<WidgetGesture, int> gesture)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(gesture);
        _host = host;
        _vertical = vertical;
        _edge = edge;
        _gesture = gesture;
        Pad(Spacing.BarItemPadding(vertical));

        // A tone's fill is a brush of its own, so a theme change draws the meters and a toned glyph again. Everything else holds the chrome brushes.
        // Moving an item along the bar can load it twice with no unload between, and the event is static, so it is joined once.
        host.Loaded += (_, _) =>
        {
            if (!_watching)
            {
                _watching = true;
                Paint.Changed += Repaint;
            }
        };
        host.Unloaded += (_, _) =>
        {
            if (_watching)
            {
                _watching = false;
                Paint.Changed -= Repaint;
            }
        };
    }

    /// <summary>True while the item is the tray.</summary>
    public bool Tray { get; private set; }

    /// <summary>True while the tray has no pinned icons, so the whole item is the chevron.</summary>
    public bool TrayBare { get; private set; }

    /// <summary>True while the tray's overflow is open and its chevron turned.</summary>
    public bool TrayOpen { get; private set; }

    /// <summary>
    /// True when the item's parts take the pointer themselves: chips, pinned tray icons, or a plugin's own drawing.
    /// The slot then draws no hover fill of its own.
    /// </summary>
    public bool OwnHits => (Tray && !TrayBare) || _strip is { Any: true } || _held;

    /// <summary>The tray's chevron, which a panel opens against. The item itself until a tray face has drawn one.</summary>
    public FrameworkElement Chevron => _chevron ?? (FrameworkElement)_host;

    /// <summary>The row of meters. A point measured in it finds a provider with <see cref="MeterAt"/>. The item itself until a face has drawn meters.</summary>
    public UIElement Meters => _meters ?? (UIElement)_host;

    /// <summary>
    /// Holds a plugin's own drawing (<see cref="IKitBar"/>) in place of a face. A later face only shows or hides the item and sets its label and tooltip.
    /// </summary>
    /// <param name="drawing">What <see cref="IKitBar.BuildBar"/> returned.</param>
    public void Hold(UIElement drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        _held = true;
        _face = NoMark;
        _drawn = null;

        // A face drawn before the plugin's own drawing arrived is let go with its parts.
        _marquee?.Close();
        _marquee = null;
        _body = null;
        _mark = null;
        _icon = null;
        _picture = null;
        _pictureBytes = null;
        _text = null;
        _menu = null;
        _weather = null;
        _readouts = null;
        _meters = null;
        _tray = null;
        _pinnedIcons = null;
        _chevron = null;
        _chevronPivot = null;
        _strip = null;
        _ranks.Clear();
        _readoutValues.Clear();
        _readoutShown.Clear();
        _shownReadouts = [];
        _shownMeters = [];
        Tray = false;
        TrayBare = false;
        _host.Content = Centered(drawing);
        Pad(Spacing.BarItemPadding(_vertical));
    }

    /// <summary>Draws <paramref name="face"/>.</summary>
    /// <param name="face">What the plugin published.</param>
    /// <returns>False when nothing on screen changed, so the slot need not paint again.</returns>
    public bool Show(BarFace face)
    {
        ArgumentNullException.ThrowIfNull(face);

        // Only the chips can differ from a chips-only face that is on screen under the same label and tooltip.
        // A hidden item has no such face, so it takes the full draw below and comes back.
        if (!_held
            && _drawn is { Glyph.Length: 0, Text.Length: 0 } shown
            && string.Equals(face.Label, shown.Label, StringComparison.Ordinal)
            && string.Equals(face.Tooltip, shown.Tooltip, StringComparison.Ordinal)
            && face.TextWidth == shown.TextWidth
            && !face.Tray
            && face.Visible
            && face.Glyph.Length == 0
            && face.Text.Length == 0
            && ReferenceEquals(face.Picture, shown.Picture)
            && _strip is { } chips
            && chips.Retarget(face.Chips))
        {
            return false;
        }

        if (ShowFigures(face))
        {
            return false;
        }

        var mark = BarFaceMark.Of(face);
        if (mark == _face)
        {
            return false;
        }

        _face = mark;
        _drawn = null;
        var appearing = face.Visible && _host.Visibility != Visibility.Visible;
        _host.Visibility = face.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (appearing && !_held)
        {
            // A face the host draws fades in, such as the update badge. A plugin that draws its own item fades that drawing.
            Motion.FadeIn(_host, UiMotion.Fast);
        }

        if (!face.Visible)
        {
            _marquee?.Close();
            return true;
        }

        // The label is for a screen reader and UI Automation. Only a face that sets a tooltip shows one on hover.
        Name(face);
        if (!string.Equals(face.Tooltip, _tooltip, StringComparison.Ordinal))
        {
            _tooltip = face.Tooltip;
            ToolTipService.SetToolTip(_host, face.Tooltip.Length > 0 ? face.Tooltip : null);
        }

        if (_held)
        {
            return true;
        }

        var body = EnsureBody();
        var text = _text!;
        var icon = _icon!;

        // A glyph item is 40 wide. 12px of padding on each side leaves a 16px box, the icon's size, so the mark cannot sit to the left of a wider content area.
        // The tray sets its own padding below.
        if (!face.Tray)
        {
            var iconSlot = face.Text.Length == 0 && face.Chips.Count == 0 && (face.Glyph.Length > 0 || face.Picture is { Length: > 0 });
            Pad(iconSlot ? GlyphPadding() : Spacing.BarItemPadding(_vertical));
        }

        _tone = face.Accent ? PanelTone.Normal : face.Tone;
        var ink = face.Accent
            ? Paint.AccentText
            : _tone != PanelTone.Normal ? Paint.Fill(_tone)
            : face.Quiet || face.Glyph == "wifi-off" ? Paint.Muted : Paint.Foreground;
        text.Foreground = ink;
        // A fixed width is for a horizontal bar. A vertical bar has no room for that box, so the line sizes to the text.
        if (!_vertical && face.TextWidth > 0 && face.Text.Length > 0 && !face.Tray)
        {
            text.Visibility = Visibility.Collapsed;
            EnsureMarquee().Show(face.Text, face.TextWidth, ink);
        }
        else
        {
            _marquee?.Close();
            text.Text = face.Text;
            text.Visibility = face.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        var pictured = ShowPicture(face.Picture);
        icon.Foreground = ink;
        if (pictured)
        {
            icon.Glyph = "";
            icon.Visibility = Visibility.Collapsed;
        }
        else
        {
            icon.Glyph = Glyphs.Named(face.Glyph);
            icon.Visibility = icon.Glyph.Length > 0 && face.Glyph != "menu" ? Visibility.Visible : Visibility.Collapsed;
        }

        _mark!.Visibility = pictured || icon.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (face.Glyph == "menu")
        {
            EnsureMenu().Visibility = Visibility.Visible;
        }
        else if (_menu is not null)
        {
            _menu.Visibility = Visibility.Collapsed;
        }

        var weather = WeatherMarks.Names(face.Glyph);
        if (weather)
        {
            var sky = EnsureWeather();
            sky.Visibility = Visibility.Visible;
            WeatherMarks.Fill(sky, face.Glyph, ink);
        }
        else if (_weather is not null)
        {
            _weather.Visibility = Visibility.Collapsed;
        }

        // A vertical bar has no room beside a glyph or a picture, so the text goes under it.
        var marked = _mark!.Visibility == Visibility.Visible || weather;
        body.Orientation = _vertical && marked && face.Text.Length > 0 ? Orientation.Vertical : Orientation.Horizontal;
        ShowReadouts(face.Readouts);
        ShowMeters(face.Meters);

        if (face.Tray)
        {
            ShowTray(face);
            return true;
        }

        if (Tray)
        {
            Tray = false;
            TrayBare = false;
            _chevron!.ClearValue(FrameworkElement.WidthProperty);
            _chevron.ClearValue(FrameworkElement.HeightProperty);
            _chevron.Background = null;
            _tray!.Visibility = Visibility.Collapsed;
        }

        if (face.Chips.Count > 0 || _strip is not null)
        {
            EnsureStrip().Show(face.Chips);
        }

        _drawn = face;
        return true;
    }

    /// <summary>
    /// Writes only the figures that moved when <paramref name="face"/> differs from the face on screen in its readout values and its label alone,
    /// as the system usage item does once a second. Nothing else on the item is touched, so the slot need not paint again.
    /// </summary>
    /// <returns>True when the face was drawn here.</returns>
    private bool ShowFigures(BarFace face)
    {
        if (_drawn is not { } drawn || _held || !face.Visible || face.Tray)
        {
            return false;
        }

        var readouts = face.Readouts;
        if (readouts.Count == 0
            || readouts.Count != _readoutValues.Count
            || readouts.Count != drawn.Readouts.Count
            || face.Meters.Count > 0
            || drawn.Meters.Count > 0
            || face.Chips.Count > 0
            || drawn.Chips.Count > 0
            || face.Accent != drawn.Accent
            || face.Quiet != drawn.Quiet
            || face.Tone != drawn.Tone
            || !string.Equals(face.Glyph, drawn.Glyph, StringComparison.Ordinal)
            || !string.Equals(face.Text, drawn.Text, StringComparison.Ordinal)
            || face.TextWidth != drawn.TextWidth
            || !ReferenceEquals(face.Picture, drawn.Picture)
            || !string.Equals(face.Tooltip, drawn.Tooltip, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 0; i < readouts.Count; i++)
        {
            if (!SameKind(readouts[i], drawn.Readouts[i]))
            {
                return false;
            }
        }

        WriteFigures(readouts);
        Name(face);
        _drawn = face;
        _face = NoMark;
        return true;
    }

    /// <summary>Sets what a screen reader and UI Automation call the item, when it changed. A face with no label keeps the name it has.</summary>
    private void Name(BarFace face)
    {
        // The label is for a screen reader and UI Automation. Only a face that sets a tooltip shows one on hover.
        var label = face.Label.Length > 0 ? face.Label : face.Tooltip;
        if (label.Length > 0 && !string.Equals(label, _name, StringComparison.Ordinal))
        {
            _name = label;
            AutomationProperties.SetName(_host, label);
        }
    }

    private void Pad(Thickness padding)
    {
        if (padding != _padding)
        {
            _padding = padding;
            _host.Padding = padding;
        }
    }

    private StackPanel EnsureBody()
    {
        if (_body is { } body)
        {
            return body;
        }

        _text = new TextBlock { Style = Paint.Style("BarTextStyle"), VerticalAlignment = VerticalAlignment.Center };
        _icon = new FontIcon
        {
            FontFamily = Fonts.Icons,
            FontSize = TypeRamp.Icon,
            Width = TypeRamp.Icon,
            Height = TypeRamp.Icon,
            Foreground = Paint.Foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The picture and the glyph share one box, so a picture takes the same gap a glyph does and a hidden glyph takes none.
        _mark = new Grid
        {
            Width = TypeRamp.Icon,
            Height = TypeRamp.Icon,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Children = { _icon },
        };
        _body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.RowGap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _mark, _text },
        };
        _ranks.Add(IconRank);
        _ranks.Add(TextRank);
        _host.Content = Centered(_body);
        return _body;
    }

    /// <summary>The fixed-width line, made the first time a face asks for one. It takes the text's place in the row.</summary>
    private TextMarquee EnsureMarquee()
    {
        if (_marquee is { } marquee)
        {
            return marquee;
        }

        _marquee = new TextMarquee();
        Place(_marquee.Element, TextRank);
        return _marquee;
    }

    /// <summary>
    /// Draws <paramref name="png"/> in the glyph's box, and hides it when the face has none.
    /// The bytes are decoded once for the array. A later face that hands back the same array keeps this image.
    /// </summary>
    /// <param name="png">PNG bytes, or null.</param>
    /// <returns>True when a picture is showing.</returns>
    private bool ShowPicture(byte[]? png)
    {
        if (png is not { Length: > 0 })
        {
            if (_picture is not null)
            {
                _picture.Visibility = Visibility.Collapsed;
            }

            _pictureBytes = null;
            return false;
        }

        var image = _picture ??= new Image
        {
            Width = TypeRamp.Icon,
            Height = TypeRamp.Icon,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (image.Parent is null)
        {
            _mark!.Children.Add(image);
        }

        if (!ReferenceEquals(png, _pictureBytes))
        {
            _pictureBytes = png;
            try
            {
                image.Source = ChipStrip.Bitmap(png);
            }
            catch (Exception)
            {
                image.Source = null;
            }
        }

        if (image.Source is null)
        {
            image.Visibility = Visibility.Collapsed;
            return false;
        }

        image.Visibility = Visibility.Visible;
        return true;
    }

    /// <summary>Puts a part made late where it would have been had every part been made at the start.</summary>
    private T Place<T>(T part, int rank)
        where T : UIElement
    {
        var body = EnsureBody();
        var at = 0;
        while (at < _ranks.Count && _ranks[at] < rank)
        {
            at++;
        }

        body.Children.Insert(at, part);
        _ranks.Insert(at, rank);
        return part;
    }

    private Grid EnsureMenu() => _menu ??= Place(BarMarks.Menu(), MenuRank);

    private Grid EnsureWeather() =>
        _weather ??= Place(new Grid { Width = TypeRamp.Icon, Height = TypeRamp.Icon, VerticalAlignment = VerticalAlignment.Center }, WeatherRank);

    private StackPanel EnsureReadouts() => _readouts ??= Place(
        new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = Spacing.PanelGap,
        },
        ReadoutsRank);

    private StackPanel EnsureMeters() => _meters ??= Place(
        new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = _vertical ? Spacing.PanelGap : Spacing.ControlGap,
        },
        MetersRank);

    private ChipStrip EnsureStrip()
    {
        if (_strip is { } strip)
        {
            return strip;
        }

        _strip = new ChipStrip(_vertical, tag => _gesture(WidgetGesture.Left, tag));
        Place(_strip.Element, StripRank);
        return _strip;
    }

    private StackPanel EnsureTray()
    {
        if (_tray is { } tray)
        {
            return tray;
        }

        _pinnedIcons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = TrayGap, VerticalAlignment = VerticalAlignment.Center };
        _chevronPivot = new Grid { Width = TypeRamp.Icon, Height = TypeRamp.Icon };
        _chevron = ChevronBox(_chevronPivot);
        _tray = Place(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _pinnedIcons, _chevron },
            },
            TrayRank);
        return _tray;
    }

    /// <summary>Paints the open-panel state on the tray's chevron.</summary>
    /// <param name="active">True while this item's panel is open.</param>
    /// <returns>True when the chevron took it, so the slot keeps its own fill.</returns>
    public bool SetActive(bool active)
    {
        _active = active;
        if (!Tray || TrayBare)
        {
            return false;
        }

        _chevron!.Background = active ? Paint.Selected : Paint.Hit;
        return true;
    }

    /// <summary>The index of the provider under <paramref name="point"/>, measured in <see cref="Meters"/>. Zero when the item has no meters.</summary>
    /// <param name="point">The point in the meter row's own coordinates.</param>
    /// <returns>The provider's index.</returns>
    public int MeterAt(Point point)
    {
        if (_meters is not { Visibility: Visibility.Visible } meters)
        {
            return 0;
        }

        for (var i = 0; i < meters.Children.Count; i++)
        {
            if (meters.Children[i] is not FrameworkElement group)
            {
                continue;
            }

            var origin = group.TransformToVisual(meters).TransformPoint(new Point(0, 0));
            var half = meters.Spacing / 2;
            var inside = _vertical
                ? point.Y < origin.Y + group.ActualHeight + half
                : point.X < origin.X + group.ActualWidth + half;
            if (inside)
            {
                return i;
            }
        }

        return Math.Max(0, meters.Children.Count - 1);
    }

    /// <summary>True when <paramref name="source"/> is a pinned tray icon, which drags into the overflow instead of moving the bar item.</summary>
    /// <param name="source">Where the press landed.</param>
    /// <returns>True when the press is inside the pinned icons.</returns>
    public bool OverPinnedIcon(object? source)
    {
        if (_pinnedIcons is null)
        {
            return false;
        }

        for (var node = source as DependencyObject; node is not null && !ReferenceEquals(node, _host); node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, _pinnedIcons))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The key of the pinned tray icon under <paramref name="source"/>, or empty when the press is elsewhere.</summary>
    /// <param name="source">Where the press landed.</param>
    /// <returns>The icon key.</returns>
    public string PinnedKey(object? source)
    {
        if (!OverPinnedIcon(source))
        {
            return "";
        }

        for (var node = source as DependencyObject; node is not null && !ReferenceEquals(node, _pinnedIcons); node = VisualTreeHelper.GetParent(node))
        {
            if (node is Button { Tag: string key })
            {
                return key;
            }
        }

        return "";
    }

    /// <summary>True when <paramref name="point"/>, in the chevron's own coordinates, lands on it.</summary>
    /// <param name="point">The point.</param>
    /// <returns>True when it is inside the chevron.</returns>
    public bool InsideChevron(Point point) =>
        _chevron is { } chevron
        && point.X >= 0 && point.Y >= 0
        && point.X < chevron.ActualWidth
        && point.Y < chevron.ActualHeight;

    private Thickness GlyphPadding() => new(Spacing.ControlPaddingX, _vertical ? Spacing.RowGap : 0, Spacing.ControlPaddingX, _vertical ? Spacing.RowGap : 0);

    private void Repaint()
    {
        if (_tone != PanelTone.Normal && _text is { } text && _icon is { } icon)
        {
            var ink = Paint.Fill(_tone);
            text.Foreground = ink;
            icon.Foreground = ink;
            _marquee?.Ink(ink);
        }

        _meterMark = "";
        ShowMeters(_shownMeters);
    }

    private void ShowTray(BarFace face)
    {
        Tray = true;
        var tray = EnsureTray();
        var pinnedIcons = _pinnedIcons!;
        var chevron = _chevron!;
        _marquee?.Close();
        _text!.Visibility = Visibility.Collapsed;
        _icon!.Visibility = Visibility.Collapsed;
        if (_mark is not null)
        {
            _mark.Visibility = Visibility.Collapsed;
        }
        if (_weather is not null)
        {
            _weather.Visibility = Visibility.Collapsed;
        }

        if (_menu is not null)
        {
            _menu.Visibility = Visibility.Collapsed;
        }

        if (_strip is not null)
        {
            _strip.Element.Visibility = Visibility.Collapsed;
        }

        tray.Visibility = Visibility.Visible;
        tray.Orientation = face.TrayVertical ? Orientation.Vertical : Orientation.Horizontal;
        pinnedIcons.Orientation = tray.Orientation;
        TrayBare = face.TrayPinned.Count == 0;
        if (TrayBare)
        {
            Pad(GlyphPadding());
            chevron.ClearValue(FrameworkElement.WidthProperty);
            chevron.ClearValue(FrameworkElement.HeightProperty);
            chevron.Background = null;
        }
        else
        {
            Pad(_vertical ? Spacing.BarItemPadding(true) : new Thickness(Spacing.RowGap, 0, Spacing.RowGap, 0));
            chevron.Width = TrayHit;
            chevron.Height = TrayHit;
            chevron.Background = _active || face.TrayExpanded ? Paint.Selected : Paint.Hit;
        }

        // The tag is the icon's place in the row. The key and the picture say which program stands there.
        // The tooltip is not part of it: one that carries a figure changes by the second, and a new button each time would break a press or a drag.
        var fingerprint = string.Join(',', face.TrayPinned.Select(chip => chip.Key)) + "|" + ChipMark.Of(face.TrayPinned);
        if (fingerprint != _drawnTray)
        {
            _drawnTray = fingerprint;
            pinnedIcons.Children.Clear();
            foreach (var chip in face.TrayPinned)
            {
                pinnedIcons.Children.Add(TrayButton(chip));
            }
        }
        else
        {
            for (var i = 0; i < face.TrayPinned.Count && i < pinnedIcons.Children.Count; i++)
            {
                if (face.TrayPinned[i].Text.Length > 0)
                {
                    AutomationProperties.SetName(pinnedIcons.Children[i], face.TrayPinned[i].Text);
                }
            }
        }

        TurnChevron(face.TrayExpanded);
    }

    /// <summary>
    /// Draws the readouts the way every other item draws a glyph and its text: each figure close beside its icon, in the bar's ink.
    /// The icons are built once; each later face only changes the figures. A figure starts at its icon and keeps a fixed width,
    /// so the item does not move as the numbers change.
    /// </summary>
    private void ShowReadouts(IReadOnlyList<BarReadout> readouts)
    {
        // An item that has never drawn a figure has no row for them.
        if (readouts.Count == 0 && _readouts is null)
        {
            return;
        }

        var row = EnsureReadouts();
        row.Visibility = readouts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var same = readouts.Count == _shownReadouts.Count;
        for (var i = 0; same && i < readouts.Count; i++)
        {
            same = SameKind(readouts[i], _shownReadouts[i]);
        }

        _shownReadouts = readouts;
        if (!same)
        {
            row.Children.Clear();
            _readoutValues.Clear();
            _readoutShown.Clear();
            foreach (var readout in readouts)
            {
                var value = new TextBlock
                {
                    Style = Paint.Style("BarTextStyle"),
                    Foreground = Paint.Foreground,
                    MinWidth = _vertical ? 0 : 28,
                    TextAlignment = _vertical ? TextAlignment.Center : TextAlignment.Left,
                };
                _readoutValues.Add(value);
                _readoutShown.Add("");
                row.Children.Add(new StackPanel
                {
                    Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal,
                    Spacing = Spacing.RowGap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { BarMarks.Readout(readout), value },
                });
            }
        }

        WriteFigures(readouts);
    }

    /// <summary>True when two readouts draw the same icon or label, so only the figure beside it can differ.</summary>
    private static bool SameKind(BarReadout one, BarReadout other) =>
        string.Equals(one.Glyph, other.Glyph, StringComparison.Ordinal) && string.Equals(one.Label, other.Label, StringComparison.Ordinal);

    /// <summary>Writes each figure that differs from the one on screen.</summary>
    private void WriteFigures(IReadOnlyList<BarReadout> readouts)
    {
        for (var i = 0; i < readouts.Count; i++)
        {
            var value = readouts[i].Value;
            if (!string.Equals(value, _readoutShown[i], StringComparison.Ordinal))
            {
                _readoutShown[i] = value;
                _readoutValues[i].Text = value;
            }
        }
    }

    /// <summary>Draws each provider's mark, and its meter and figure when asked.</summary>
    private void ShowMeters(IReadOnlyList<BarMeter> meters)
    {
        // An item with no meters, now or before, has nothing to draw or to clear.
        if (meters.Count == 0 && _shownMeters.Count == 0)
        {
            return;
        }

        var row = EnsureMeters();
        _shownMeters = meters;
        row.Visibility = meters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var mark = (AgentMarks.LightSurface ? "l" : "d") + string.Join('\u001f', meters.Select(meter =>
            meter.Mark + ":" + meter.Figure + ":" + Math.Round(meter.Fill * 100) + ":" + Math.Round(meter.Tick * 100) + ":" + meter.Tone + ":" + meter.Shows));
        if (mark == _meterMark)
        {
            return;
        }

        _meterMark = mark;
        row.Children.Clear();
        foreach (var meter in meters)
        {
            var group = new StackPanel
            {
                Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal,
                Spacing = _vertical ? 3 : 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Paint.Hit,
            };
            group.Children.Add(AgentMarks.Element(meter.Mark, TypeRamp.Icon));
            if (meter.Shows == BarShows.Full && !_vertical)
            {
                group.Children.Add(BarMarks.Track(meter));
            }

            if (meter.Shows != BarShows.Icon)
            {
                group.Children.Add(new TextBlock
                {
                    Text = meter.Figure,
                    Style = Paint.Style("BarTextStyle"),
                    Foreground = meter.Tone is PanelTone.Warn or PanelTone.Critical ? AgentMarks.Fill(meter.Tone) : Paint.Foreground,
                    FontSize = _vertical ? TypeRamp.Caption - 1 : TypeRamp.Caption,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            row.Children.Add(group);
        }
    }

    private void TurnChevron(bool open)
    {
        var target = BarChevron.Angle(_edge, open);
        var pivot = _chevronPivot!;
        if (!_chevronPlaced)
        {
            Motion.Turn(pivot, target, animate: false);
            _chevronPlaced = true;
            TrayOpen = open;
            return;
        }

        if (open == TrayOpen)
        {
            return;
        }

        TrayOpen = open;
        Motion.Turn(pivot, target, animate: Motion.Enabled());
    }

    private Button TrayButton(BarChip chip)
    {
        var tag = chip.Tag;
        var button = new Button
        {
            Style = Paint.Style("BarButtonStyle"),
            Width = TrayHit,
            Height = TrayHit,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Content = chip.Icons.Count > 0
                ? new Image { Source = ChipStrip.Bitmap(chip.Icons[0]), Width = TypeRamp.Icon, Height = TypeRamp.Icon }
                : new TextBlock
                {
                    Text = chip.Text.Length > 0 ? chip.Text[..1] : "•",
                    Style = Paint.Style("BarTextStyle"),
                    Foreground = Paint.Foreground,
                },
        };
        if (chip.Text.Length > 0)
        {
            AutomationProperties.SetName(button, chip.Text);
        }

        button.Tag = chip.Key;
        button.Click += (_, _) => _gesture(WidgetGesture.Left, tag);
        button.Tapped += (_, args) => args.Handled = true;
        // Handled here, so the item's own right-click, which opens the tray manager, does not run too.
        button.RightTapped += (_, args) =>
        {
            args.Handled = true;
            _gesture(WidgetGesture.Right, tag);
        };
        return button;
    }

    private static Border ChevronBox(Grid pivot)
    {
        var mark = new FontIcon
        {
            FontFamily = Fonts.Icons,
            Glyph = "\uE70E",
            FontSize = TypeRamp.Icon,
            Foreground = Paint.Foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        pivot.HorizontalAlignment = HorizontalAlignment.Center;
        pivot.VerticalAlignment = VerticalAlignment.Center;
        pivot.Children.Add(mark);
        return new Border
        {
            CornerRadius = Corners.Control,
            Background = Paint.Hit,
            BackgroundTransition = new BrushTransition { Duration = UiMotion.Faster },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = pivot,
        };
    }

    /// <summary>
    /// The slot stretches its child. A horizontal stack then pins the glyph to the left of the leftover width, so it sits in the middle column.
    /// </summary>
    private static Grid Centered(UIElement drawing)
    {
        var body = drawing as FrameworkElement ?? new Border { Child = drawing };
        var centered = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        centered.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        centered.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        centered.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(body, 1);
        centered.Children.Add(body);
        return centered;
    }
}
