using System.Globalization;

namespace Winshell.Core;

/// <summary>What a tray panel command asks for.</summary>
public enum TrayVerb
{
    /// <summary>Not a tray command.</summary>
    None,

    /// <summary>Activate the icon, as a left click on the Windows tray does.</summary>
    Open,

    /// <summary>Open the icon's own context menu, as a right-click on the Windows tray does.</summary>
    Menu,

    /// <summary>Pin or unpin the icon on the bar.</summary>
    Pin,

    /// <summary>Hide or show the icon.</summary>
    Hide,

    /// <summary>Write the overflow order. Each key in it leaves the bar and is shown in the panel.</summary>
    Order,

    /// <summary>Land one icon on a cell of the overflow. It leaves the bar.</summary>
    Place,
}

/// <summary>A command from the tray overflow or the tray manager.</summary>
/// <param name="Verb">What to do.</param>
/// <param name="Tag">The icon's chip tag, for <see cref="TrayVerb.Open"/> and <see cref="TrayVerb.Menu"/>. Zero otherwise.</param>
/// <param name="Key">The icon key, for <see cref="TrayVerb.Pin"/> and <see cref="TrayVerb.Hide"/>. Empty otherwise.</param>
public readonly record struct TrayCommand(TrayVerb Verb, int Tag, string Key)
{
    private const string OpenPrefix = "icon:";
    private const string MenuPrefix = "menu:";
    private const string PinPrefix = "pin:";
    private const string HidePrefix = "hide:";
    private const string OrderPrefix = "order:";

    /// <summary>Separates keys in an <see cref="TrayVerb.Order"/> command. A tooltip can contain a comma or a colon, so those stay in the key.</summary>
    public const char OrderSeparator = '\u001e';

    /// <summary>Keys of an <see cref="TrayVerb.Order"/> command, in the order the overflow should show them. Empty otherwise.</summary>
    public IReadOnlyList<string> Keys { get; init; } = [];

    /// <summary>The command a left click on an overflow icon sends.</summary>
    /// <param name="tag">The icon's chip tag.</param>
    /// <returns>Such as <c>icon:3</c>.</returns>
    public static string Open(int tag) => OpenPrefix + tag.ToString(CultureInfo.InvariantCulture);

    /// <summary>The command a right-click on an overflow icon sends.</summary>
    /// <param name="tag">The icon's chip tag.</param>
    /// <returns>Such as <c>menu:3</c>.</returns>
    public static string Menu(int tag) => MenuPrefix + tag.ToString(CultureInfo.InvariantCulture);

    /// <summary>The manager's pin button for <paramref name="key"/>.</summary>
    /// <param name="key">The icon key.</param>
    /// <returns>Such as <c>pin:Discord</c>.</returns>
    public static string Pin(string key) => PinPrefix + key;

    /// <summary>The manager's hide button for <paramref name="key"/>.</summary>
    /// <param name="key">The icon key.</param>
    /// <returns>Such as <c>hide:Discord</c>.</returns>
    public static string Hide(string key) => HidePrefix + key;

    /// <summary>The overflow after a drop. The keys are the whole drawer, in their new order.</summary>
    /// <param name="keys">Drawer keys, including one that just left the bar.</param>
    /// <returns>The order command.</returns>
    public static string Order(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return OrderPrefix + string.Join(OrderSeparator, keys);
    }

    /// <summary>A drop of <paramref name="key"/> onto cell <paramref name="index"/> of the overflow.</summary>
    /// <param name="index">The cell. Zero is the first. One past the last icon appends.</param>
    /// <param name="key">The icon key. A tooltip may contain a colon, so only the first colon is the separator.</param>
    /// <returns>Such as <c>place:1:Discord</c>.</returns>
    public static string Place(int index, string key) =>
        "place:" + index.ToString(CultureInfo.InvariantCulture) + ":" + key;

    /// <summary>The text a drag carries for <paramref name="key"/>.</summary>
    /// <param name="key">The icon key.</param>
    /// <returns>The drag payload.</returns>
    public static string Drag(string key) => "winoma.tray:" + key;

    /// <summary>Reads a drag payload. Anything else is not a tray icon.</summary>
    /// <param name="text">What the drag carried.</param>
    /// <param name="key">The icon key, when this returns true.</param>
    /// <returns>True when <paramref name="text"/> names a tray icon.</returns>
    public static bool TryDrag(string? text, out string key)
    {
        const string prefix = "winoma.tray:";
        if (text is not null && text.StartsWith(prefix, StringComparison.Ordinal) && text.Length > prefix.Length)
        {
            key = text[prefix.Length..];
            return true;
        }

        key = "";
        return false;
    }

    /// <summary>Reads <paramref name="command"/>. Anything else, including a tag that is not a positive number, is <see cref="TrayVerb.None"/>.</summary>
    /// <param name="command">A panel command.</param>
    /// <returns>The verb and its tag or key.</returns>
    public static TrayCommand Parse(string? command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return default;
        }

        if (command.StartsWith(OpenPrefix, StringComparison.Ordinal))
        {
            return Tagged(TrayVerb.Open, command[OpenPrefix.Length..]);
        }

        if (command.StartsWith(MenuPrefix, StringComparison.Ordinal))
        {
            return Tagged(TrayVerb.Menu, command[MenuPrefix.Length..]);
        }

        if (command.StartsWith(PinPrefix, StringComparison.Ordinal))
        {
            return new TrayCommand(TrayVerb.Pin, 0, command[PinPrefix.Length..]);
        }

        if (command.StartsWith(HidePrefix, StringComparison.Ordinal))
        {
            return new TrayCommand(TrayVerb.Hide, 0, command[HidePrefix.Length..]);
        }

        if (command.StartsWith("place:", StringComparison.Ordinal))
        {
            var body = command["place:".Length..];
            var split = body.IndexOf(':');
            if (split > 0
                && int.TryParse(body[..split], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                return new TrayCommand(TrayVerb.Place, index, body[(split + 1)..]);
            }

            return default;
        }

        if (command.StartsWith(OrderPrefix, StringComparison.Ordinal))
        {
            var body = command[OrderPrefix.Length..];
            var keys = body.Length == 0
                ? []
                : body.Split(OrderSeparator).Where(key => key.Length > 0).ToList();
            return new TrayCommand(TrayVerb.Order, 0, "") { Keys = keys };
        }

        return default;
    }

    private static TrayCommand Tagged(TrayVerb verb, string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var tag) && tag > 0
            ? new TrayCommand(verb, tag, "")
            : default;
}
