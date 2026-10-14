namespace Winshell.Core;

/// <summary>A glyph a plugin keeps in the indicator cluster through <see cref="IPluginContext.SetIndicator"/>.</summary>
/// <param name="Glyph">Named icon, such as <c>mic</c>.</param>
/// <param name="Label">What a screen reader announces.</param>
/// <param name="Active">True while the plugin is busy. An active glyph is drawn in the accent color and shows ahead of an idle one.</param>
/// <param name="Command">The plugin command a click sends. Empty does nothing.</param>
public sealed record PluginIndicator(string Glyph, string Label, bool Active, string Command = "")
{
    /// <summary>Hover text. Empty shows none.</summary>
    public string Tooltip { get; init; } = "";
}

/// <summary>An indicator and its owner: the plugin that published it, or the Host for a mode no plugin owns (<see cref="HostIndicator"/>).</summary>
/// <param name="Plugin">Plugin id, or a <c>host:</c> name for one the Host keeps.</param>
/// <param name="Indicator">What it shows.</param>
public sealed record OwnedIndicator(string Plugin, PluginIndicator Indicator);

/// <summary>
/// The Indicators the Host keeps itself, for a mode no plugin owns. The owner's name starts with <c>host:</c>, which no plugin id can,
/// and a click runs the Indicator's command as a Chord command. The Host publishes one with <see cref="PluginRuntime.SetHostIndicator"/>.
/// </summary>
public static class HostIndicator
{
    /// <summary>What every Host owner's name starts with.</summary>
    public const string Prefix = "host:";

    /// <summary>The owner of the Idle lock Indicator.</summary>
    public const string IdleLock = Prefix + "idle-lock";

    /// <summary>The owner of the recording Indicator.</summary>
    public const string Recording = Prefix + "recording";

    /// <summary>True when <paramref name="owner"/> names an Indicator the Host keeps.</summary>
    /// <param name="owner">An owner, as <see cref="OwnedIndicator.Plugin"/> has it.</param>
    /// <returns>True for a <c>host:</c> name.</returns>
    public static bool Owns(string owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return owner.StartsWith(Prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Idle lock Indicator: the coffee cup, active while Idle lock is switched off, so a Host that will not lock by itself says so.
    /// A click runs <c>idle-lock</c>, which switches it.
    /// </summary>
    /// <param name="enabled">True while Idle lock is on.</param>
    /// <returns>The Indicator.</returns>
    public static PluginIndicator OfIdleLock(bool enabled) =>
        new("coffee", enabled ? "Idle lock is on" : "Idle lock is off", Active: !enabled, Command: "idle-lock");

    /// <summary>
    /// The recording Indicator: active while Winoma records the screen, so a recording is not left running (ADR 0084).
    /// A click runs <c>record</c>, which stops the recording that runs, as a second press of its Chord does, and opens the picker when none does.
    /// </summary>
    /// <param name="recording">True while a recording runs.</param>
    /// <returns>The Indicator.</returns>
    public static PluginIndicator OfRecording(bool recording) =>
        new("record", recording ? "Recording the screen" : "Not recording", Active: recording, Command: "record")
        {
            Tooltip = recording ? "Stop the recording" : "",
        };
}

/// <summary>
/// Where the person is on the Bar, which is what brings the idle Indicators up. It is one state for every monitor's Bar,
/// so the idle Indicators show on all of them together (ADR 0080).
/// </summary>
/// <param name="Pointer">True while the pointer is anywhere on a Bar, on an item or between two.</param>
/// <param name="Keyboard">True while the Bar has the keyboard: a Panel it opened is up (<see cref="PanelRoster.BarHasKeyboard"/>).</param>
public readonly record struct BarAttention(bool Pointer, bool Keyboard)
{
    /// <summary>The one state for every Bar: the pointer is on the Bar while it is on any of them.</summary>
    /// <param name="pointers">Whether the pointer is on each Bar.</param>
    /// <param name="keyboard">True while a Panel the Bar opened is up.</param>
    /// <returns>What every Bar's indicator cluster goes by.</returns>
    public static BarAttention Across(IEnumerable<bool> pointers, bool keyboard)
    {
        ArgumentNullException.ThrowIfNull(pointers);
        var pointer = false;
        foreach (var on in pointers)
        {
            pointer |= on;
        }

        return new BarAttention(pointer, keyboard);
    }
}

/// <summary>The indicators plugins have published, for the indicator cluster.</summary>
public interface IIndicatorServices
{
    /// <summary>Where the Bar's pointer and keyboard are now. A reader is ticked when either moves.</summary>
    BarAttention Bar { get; }

    /// <summary>Every published indicator, in the order the plugins first published one.</summary>
    IReadOnlyList<OwnedIndicator> Indicators { get; }

    /// <summary>Sends <paramref name="plugin"/>'s indicator command, the way a click does. One the Host keeps runs its command as a Chord command.</summary>
    /// <param name="plugin">The indicator's owner.</param>
    void Press(string plugin);
}

/// <summary>Published indicators, safe to change from any thread.</summary>
public sealed class IndicatorRoster
{
    private readonly object _gate = new();
    private readonly List<OwnedIndicator> _items = [];

    /// <summary>Changes when an indicator is set or cleared.</summary>
    public int Revision { get; private set; }

    /// <summary>Sets or clears <paramref name="plugin"/>'s indicator. A plugin keeps the place it first took.</summary>
    /// <param name="plugin">Plugin id.</param>
    /// <param name="indicator">The glyph, or null to remove it.</param>
    /// <returns>True when the roster changed.</returns>
    public bool Set(string plugin, PluginIndicator? indicator)
    {
        ArgumentException.ThrowIfNullOrEmpty(plugin);
        lock (_gate)
        {
            var at = _items.FindIndex(item => item.Plugin.Equals(plugin, StringComparison.Ordinal));
            if (indicator is null)
            {
                if (at < 0)
                {
                    return false;
                }

                _items.RemoveAt(at);
                Revision++;
                return true;
            }

            var owned = new OwnedIndicator(plugin, indicator);
            if (at >= 0)
            {
                if (_items[at] == owned)
                {
                    return false;
                }

                _items[at] = owned;
            }
            else
            {
                _items.Add(owned);
            }

            Revision++;
            return true;
        }
    }

    /// <summary>A copy of the roster.</summary>
    /// <returns>Indicators in first-published order.</returns>
    public IReadOnlyList<OwnedIndicator> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }

    /// <summary>The indicator <paramref name="plugin"/> published, or null.</summary>
    /// <param name="plugin">Plugin id.</param>
    /// <returns>Its indicator, or null.</returns>
    public PluginIndicator? Of(string plugin)
    {
        lock (_gate)
        {
            foreach (var item in _items)
            {
                if (item.Plugin.Equals(plugin, StringComparison.Ordinal))
                {
                    return item.Indicator;
                }
            }

            return null;
        }
    }
}
