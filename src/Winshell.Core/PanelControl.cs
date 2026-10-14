namespace Winshell.Core;

/// <summary>
/// A Kit control as the CLI sees it: a line for <c>winoma desktop</c>, and what pressing it does for <c>winoma summon</c> (ADR 0045).
/// Every interactive control has a command name, so an agent can find it in the outline and press it.
/// </summary>
public sealed class PanelControl
{
    private readonly Func<PanelLine> _describe;
    private readonly Action<double?, string?>? _press;

    /// <summary>Creates a control.</summary>
    /// <param name="command">What the control is pressed by. Empty for a control that only shows something.</param>
    /// <param name="describe">The control as it stands now.</param>
    /// <param name="press">Presses it with a slider, switch, or stepper value, or a field's text. Null for a control that only shows something.</param>
    public PanelControl(string command, Func<PanelLine> describe, Action<double?, string?>? press = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(describe);
        if (press is not null && command.Length == 0)
        {
            throw new ArgumentException("A control that can be pressed needs a command name.", nameof(command));
        }

        Command = command;
        _describe = describe;
        _press = press;
    }

    /// <summary>What the control is pressed by. Empty when it only shows something.</summary>
    public string Command { get; }

    /// <summary>True when <c>winoma summon</c> can press it.</summary>
    public bool Pressable => _press is not null;

    /// <summary>The control as <c>winoma desktop</c> prints it.</summary>
    /// <returns>One outline line.</returns>
    public PanelLine Describe() => _describe();

    /// <summary>Presses the control as a click, a drag, or Enter would.</summary>
    /// <param name="value">A slider, switch, or stepper value. Null for a plain click.</param>
    /// <param name="text">A text field's text. Null otherwise.</param>
    public void Press(double? value, string? text) => _press?.Invoke(value, text);

    /// <summary>
    /// The first control in <paramref name="controls"/> that <paramref name="command"/> presses, in the order the panel shows them.
    /// Null when none does, and the command then goes to the plugin's own handler.
    /// </summary>
    /// <param name="controls">The open panel's controls, top to bottom.</param>
    /// <param name="command">The command <c>winoma summon</c> sent.</param>
    /// <returns>The control, or null.</returns>
    public static PanelControl? Find(IEnumerable<PanelControl> controls, string command)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(command);
        return command.Length == 0
            ? null
            : controls.FirstOrDefault(control => control.Pressable && string.Equals(control.Command, command, StringComparison.Ordinal));
    }
}
