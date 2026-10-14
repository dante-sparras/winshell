namespace Winshell.Core;

/// <summary>A plugin the bar can load from an assembly.</summary>
public interface IWinomaPlugin
{
    /// <summary>Manifest id.</summary>
    string Id { get; }

    /// <summary>
    /// Called once after construction. A plugin whose manifest sets <c>keepLoaded</c> hears it again after a remount
    /// that gave it a new bar slot or took its slot away, with the context for where it is now.
    /// </summary>
    void Attach(IPluginContext context);

    /// <summary>A pointer gesture on the widget. <paramref name="tag"/> is the chip that was hit, or 0.</summary>
    void OnGesture(WidgetGesture gesture, int tag);

    /// <summary>A panel row sent <paramref name="command"/>. <paramref name="value"/> is set for a slider.</summary>
    void OnPanel(string command, double? value);

    /// <summary>A panel field sent <paramref name="command"/> with <paramref name="text"/>.</summary>
    /// <param name="command">Field command.</param>
    /// <param name="text">What was typed. A security key is not written to the log.</param>
    void OnText(string command, string text);

    /// <summary>Refreshes the face. The host calls this on a bar loop turn when the plugin is due. See <see cref="NextTick"/>.</summary>
    void Tick();

    /// <summary>
    /// How long the host may wait after a tick before the next one. Zero or less, the default, ticks on every loop turn.
    /// The host ticks sooner after a gesture, a panel or toast command, or a plugin command, and on every turn while the panel is open.
    /// </summary>
    /// <returns>The wait, read just after each <see cref="Tick"/>.</returns>
    TimeSpan NextTick() => TimeSpan.Zero;
}
