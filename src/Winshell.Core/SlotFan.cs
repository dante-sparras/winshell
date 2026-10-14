namespace Winshell.Core;

/// <summary>
/// One plugin's slot on every Bar (ADR 0080). A plugin has one context and publishes one face, and each monitor's Bar draws its own copy of it:
/// this hands the face, and the plugin to bind, to the slot on each Bar. The plugin cannot tell how many Bars there are.
/// </summary>
public sealed class SlotFan : IPluginSlot
{
    private readonly IReadOnlyList<IPluginSlot> _slots;
    private readonly Func<string, int?, int>? _home;

    // The Bar whose slot is passing a gesture on right now, or -1. A plugin opens its panel from inside the click that asks for it.
    private int _pressed = -1;

    /// <summary>Creates the fan over <paramref name="slots"/>.</summary>
    /// <param name="slots">The plugin's slot on each Bar, the first Bar's first. At least one.</param>
    /// <param name="home">
    /// Which Bar a panel opens on: the plugin's id and the Bar a gesture came from, or null when nothing on a Bar asked for the panel.
    /// <see cref="PanelSession.BarFor"/> is the rule. With none given, a panel nothing clicked opens under the first Bar.
    /// </param>
    public SlotFan(IReadOnlyList<IPluginSlot> slots, Func<string, int?, int>? home = null)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            throw new ArgumentException("A plugin's slot is on at least one Bar.", nameof(slots));
        }

        _slots = slots;
        _home = home;
    }

    /// <inheritdoc />
    /// <remarks>The runtime has dropped a face that did not change before it comes here, so a tick that changes nothing draws on no Bar.</remarks>
    public void SetFace(BarFace face)
    {
        foreach (var slot in _slots)
        {
            slot.SetFace(face);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A panel opened from inside a gesture opens under the Bar that gesture came from. One nothing on a Bar asked for,
    /// such as a Chord's, opens where a panel is already open, and otherwise on the monitor with the keyboard.
    /// A Bar that is not there is the first.
    /// </remarks>
    public void OpenPanel(IWinomaPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        int? clicked = _pressed >= 0 ? _pressed : null;
        var bar = _home?.Invoke(plugin.Id, clicked) ?? clicked ?? 0;
        _slots[bar >= 0 && bar < _slots.Count ? bar : 0].OpenPanel(plugin);
    }

    /// <inheritdoc />
    /// <remarks>Every Bar's slot is bound, so a plugin that draws its own item is asked to build it once for each Bar.</remarks>
    public void Bind(IWinomaPlugin input, IWinomaPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(input);
        for (var i = 0; i < _slots.Count; i++)
        {
            _slots[i].Bind(new From(this, i, input), plugin);
        }
    }

    /// <summary>The plugin as one Bar's slot reaches it: a gesture is remembered as that Bar's for as long as the plugin handles it.</summary>
    private sealed class From(SlotFan fan, int bar, IWinomaPlugin input) : IWinomaPlugin
    {
        public string Id => input.Id;

        public void Attach(IPluginContext context) => input.Attach(context);

        public void OnGesture(WidgetGesture gesture, int tag)
        {
            var outer = fan._pressed;
            fan._pressed = bar;
            try
            {
                input.OnGesture(gesture, tag);
            }
            finally
            {
                fan._pressed = outer;
            }
        }

        public void OnPanel(string command, double? value) => input.OnPanel(command, value);

        public void OnText(string command, string text) => input.OnText(command, text);

        public void Tick() => input.Tick();

        public TimeSpan NextTick() => input.NextTick();
    }
}
