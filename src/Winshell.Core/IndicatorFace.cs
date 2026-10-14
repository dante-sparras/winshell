namespace Winshell.Core;

/// <summary>What the indicator cluster draws from the Indicators that were published.</summary>
public static class IndicatorFace
{
    /// <summary>The Panel command a click on an Indicator sends the cluster, before its owner's id.</summary>
    public const string PressPrefix = "press:";

    /// <summary>
    /// The Panel command that stands in for the pointer, so the idle Indicators can be seen with no pointer on the Bar:
    /// alone or with a value of 1 it is the pointer coming, and with 0 the pointer going.
    /// </summary>
    public const string HoverCommand = "hover";

    /// <summary>
    /// Whether the cluster shows its idle Indicators: while the pointer is anywhere on the Bar, and while the Bar has the keyboard.
    /// The pointer crossing from one item to the next is still on the Bar, so it changes nothing here.
    /// </summary>
    /// <param name="bar">Where the Bar's pointer and keyboard are.</param>
    /// <param name="standIn">True while <see cref="HoverCommand"/> stands in for the pointer.</param>
    /// <returns>True when the idle Indicators are drawn too.</returns>
    public static bool Reveals(BarAttention bar, bool standIn) => standIn || bar.Pointer || bar.Keyboard;

    /// <summary>
    /// The Indicators the cluster draws: every active one, and the others too while they are revealed (<see cref="Reveals"/>).
    /// An active one is drawn in the accent color and the others dimmed.
    /// </summary>
    /// <param name="indicators">Published Indicators, in roster order.</param>
    /// <param name="revealed">True while the idle Indicators are drawn too.</param>
    /// <returns>The Indicators to draw, in roster order. Empty when none is active and the idle ones are hidden.</returns>
    public static IReadOnlyList<OwnedIndicator> Pick(IReadOnlyList<OwnedIndicator> indicators, bool revealed)
    {
        ArgumentNullException.ThrowIfNull(indicators);
        if (revealed)
        {
            return indicators;
        }

        var active = new List<OwnedIndicator>(indicators.Count);
        foreach (var item in indicators)
        {
            if (item.Indicator.Active)
            {
                active.Add(item);
            }
        }

        return active;
    }

    /// <summary>
    /// The cluster's own face. The cluster draws its glyphs itself, so the face only shows or hides the item and names it.
    /// It is hidden while nothing is drawn, so an idle cluster leaves no empty square. It shows while an Indicator is active,
    /// and while the idle ones are revealed.
    /// </summary>
    /// <param name="indicators">Published Indicators, in roster order.</param>
    /// <param name="revealed">True while the idle Indicators are drawn too.</param>
    /// <returns>A face named for the modes that are on. Hidden when nothing is drawn.</returns>
    public static BarFace Of(IReadOnlyList<OwnedIndicator> indicators, bool revealed)
    {
        ArgumentNullException.ThrowIfNull(indicators);
        if (Pick(indicators, revealed).Count == 0)
        {
            return new BarFace { Visible = false };
        }

        var on = string.Join(", ", Pick(indicators, revealed: false).Select(item => item.Indicator.Label));
        return new BarFace { Label = on.Length > 0 ? on : "Indicators" };
    }

    /// <summary>What <c>winoma desktop</c> prints under <c>plugins</c> and the cluster's id.</summary>
    /// <param name="indicators">Published Indicators, in roster order.</param>
    /// <param name="bar">Where the Bar's pointer and keyboard are.</param>
    /// <param name="standIn">True while <see cref="HoverCommand"/> stands in for the pointer.</param>
    /// <returns>
    /// <c>active</c> and <c>shown</c>, each the owners joined by commas. <c>hover</c> is true while the pointer is on the Bar or stood in for,
    /// and <c>keyboard</c> while the Bar has the keyboard.
    /// </returns>
    public static IReadOnlyDictionary<string, object> Facts(IReadOnlyList<OwnedIndicator> indicators, BarAttention bar, bool standIn)
    {
        ArgumentNullException.ThrowIfNull(indicators);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["active"] = Owners(Pick(indicators, revealed: false)),
            ["shown"] = Owners(Pick(indicators, Reveals(bar, standIn))),
            ["hover"] = standIn || bar.Pointer,
            ["keyboard"] = bar.Keyboard,
        };
    }

    /// <summary>The Panel command a click on <paramref name="owner"/>'s Indicator sends the cluster.</summary>
    /// <param name="owner">The Indicator's owner, as <see cref="OwnedIndicator.Plugin"/> has it.</param>
    /// <returns><c>press:</c> and the owner.</returns>
    public static string Press(string owner) => PressPrefix + owner;

    /// <summary>Whether <see cref="HoverCommand"/> with <paramref name="value"/> is the pointer coming.</summary>
    /// <param name="value">What <c>winoma summon</c> sent with the command, or null for none.</param>
    /// <returns>True for no value and for one of a half or more.</returns>
    public static bool Hovers(double? value) => value is not { } sent || sent >= 0.5;

    /// <summary>The owner a Panel command presses, or null when the command is not a press.</summary>
    /// <param name="command">A Panel command the cluster heard.</param>
    /// <returns>The owner's id, or null.</returns>
    public static string? Pressed(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.StartsWith(PressPrefix, StringComparison.Ordinal) && command.Length > PressPrefix.Length
            ? command[PressPrefix.Length..]
            : null;
    }

    private static string Owners(IReadOnlyList<OwnedIndicator> items) => string.Join(',', items.Select(item => item.Plugin));
}
