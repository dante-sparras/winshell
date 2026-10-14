using System.Globalization;

namespace Winshell.Core;

/// <summary>A key a Panel's selection hears.</summary>
public enum PanelKey
{
    /// <summary>The up arrow.</summary>
    Up,

    /// <summary>The down arrow.</summary>
    Down,

    /// <summary>The left arrow.</summary>
    Left,

    /// <summary>The right arrow.</summary>
    Right,

    /// <summary>Return.</summary>
    Enter,
}

/// <summary>What a key did in a Panel.</summary>
/// <param name="Used">True when the selection took the key, so nothing else in the Panel hears it.</param>
/// <param name="Command">The Panel command the key sends, as a click on the selected control would. Null when it sends none.</param>
/// <param name="Value">What goes with the command: a slider's new value, a switch's 1 or 0, a stepper's -1 or 1. Null for a plain click.</param>
public readonly record struct PanelStroke(bool Used, string? Command = null, double? Value = null);

/// <summary>
/// The control the arrow keys are on in an open Panel, and what each key does there (ADR 0045).
/// It reads the Panel as <c>winoma desktop</c> prints it, so every control that reports a command can be reached,
/// whichever plugin drew it. Up and Down move between controls, and take a row of chips or tabs as one.
/// Left and Right move along that row, change a slider, or step a stepper. Return sends the selected control's command.
/// </summary>
public sealed class PanelSelection
{
    private const string Slider = "slider";
    private const string Stepper = "stepper";
    private const string Switch = "switch";
    private const string Chips = "choice";
    private const string Tabs = "tabs";
    private const int SliderSteps = 20;

    private int _index = -1;
    private string _command = "";

    /// <summary>
    /// True from the first move until <see cref="Clear"/>, or until the Panel is read with no control in it.
    /// While false nothing is selected, and a host need not read the Panel to know it.
    /// </summary>
    public bool Active => _index >= 0;

    /// <summary>Reads a key by the name <c>winoma summon &lt;id&gt; key --text</c> sends: Up, Down, Left, Right, Enter, or Return.</summary>
    /// <param name="text">The name, in any case.</param>
    /// <param name="key">The key, when the name is one.</param>
    /// <returns>False for any other text, such as a key with a modifier.</returns>
    public static bool TryKey(string? text, out PanelKey key)
    {
        (var known, key) = text?.Trim().ToUpperInvariant() switch
        {
            "UP" => (true, PanelKey.Up),
            "DOWN" => (true, PanelKey.Down),
            "LEFT" => (true, PanelKey.Left),
            "RIGHT" => (true, PanelKey.Right),
            "ENTER" or "RETURN" => (true, PanelKey.Enter),
            _ => (false, default(PanelKey)),
        };
        return known;
    }

    /// <summary>Presses <paramref name="key"/> in a Panel that shows <paramref name="rows"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="rows">The Panel now, top to bottom.</param>
    /// <param name="held">True when the key was already down. A held arrow moves again, and a held Return presses once.</param>
    /// <returns>Whether the key was used, and the command it sends.</returns>
    public PanelStroke Press(PanelKey key, IReadOnlyList<PanelLine> rows, bool held = false)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var stops = Stops(rows);
        var at = Resolve(stops);
        if (stops.Count == 0)
        {
            return default;
        }

        if (key is PanelKey.Up or PanelKey.Down)
        {
            Put(stops, Vertical(stops, at, key == PanelKey.Down ? 1 : -1));
            return new PanelStroke(true);
        }

        if (at < 0)
        {
            return default;
        }

        if (key == PanelKey.Enter)
        {
            return Return(stops[at].Line, held);
        }

        var direction = key == PanelKey.Left ? -1 : 1;
        if (stops[at].Strip < 0)
        {
            return Change(stops[at].Line, direction);
        }

        var beside = at + direction;
        if (beside >= 0 && beside < stops.Count && stops[beside].Strip == stops[at].Strip)
        {
            Put(stops, beside);
        }

        return new PanelStroke(true);
    }

    /// <summary>The selected control in <paramref name="rows"/>, or null when none is selected.</summary>
    /// <param name="rows">The Panel now, top to bottom.</param>
    /// <returns>The control's line.</returns>
    public PanelLine? Selected(IReadOnlyList<PanelLine> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var stops = Stops(rows);
        return Resolve(stops) is >= 0 and var at ? stops[at].Line : null;
    }

    /// <summary>
    /// Where the selected control stands among the controls of <paramref name="rows"/> that have a command,
    /// counted from zero in the order the Panel prints them, a row before the rows inside it. -1 when none is selected.
    /// </summary>
    /// <param name="rows">The Panel now, top to bottom.</param>
    /// <returns>The place, or -1.</returns>
    public int At(IReadOnlyList<PanelLine> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Resolve(Stops(rows));
    }

    /// <summary>
    /// Puts the selection on the control at <paramref name="place"/>, counted as <see cref="At"/> counts,
    /// such as the one the pointer last pressed. A place the Panel does not have changes nothing.
    /// </summary>
    /// <param name="rows">The Panel now, top to bottom.</param>
    /// <param name="place">The control's place among those with a command.</param>
    public void Select(IReadOnlyList<PanelLine> rows, int place)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var stops = Stops(rows);
        if (place >= 0 && place < stops.Count)
        {
            Put(stops, place);
        }
    }

    /// <summary>Selects nothing, as when the pointer takes over.</summary>
    public void Clear()
    {
        _index = -1;
        _command = "";
    }

    private void Put(List<Stop> stops, int at)
    {
        _index = at;
        _command = stops[at].Line.Command ?? "";
    }

    /// <summary>
    /// Finds the selected control in the Panel as it is now. A control that moved is followed by its command,
    /// to the nearest one that sends it. One that is gone leaves the selection at its place.
    /// </summary>
    private int Resolve(List<Stop> stops)
    {
        if (_index < 0 || stops.Count == 0)
        {
            Clear();
            return -1;
        }

        if (_index < stops.Count && Sends(stops[_index], _command))
        {
            return _index;
        }

        for (var away = 1; away < stops.Count; away++)
        {
            foreach (var at in (ReadOnlySpan<int>)[_index - away, _index + away])
            {
                if (at >= 0 && at < stops.Count && Sends(stops[at], _command))
                {
                    _index = at;
                    return at;
                }
            }
        }

        Put(stops, Math.Min(_index, stops.Count - 1));
        return _index;
    }

    private static bool Sends(Stop stop, string command) => string.Equals(stop.Line.Command, command, StringComparison.Ordinal);

    /// <summary>The control one row up or down from <paramref name="from"/>. A row of chips is one row, entered at its chosen chip.</summary>
    private static int Vertical(List<Stop> stops, int from, int direction)
    {
        if (from < 0)
        {
            return Enter(stops, direction > 0 ? 0 : stops.Count - 1);
        }

        var to = from + direction;
        while (to >= 0 && to < stops.Count && stops[from].Strip >= 0 && stops[to].Strip == stops[from].Strip)
        {
            to += direction;
        }

        return to < 0 || to >= stops.Count ? from : Enter(stops, to);
    }

    private static int Enter(List<Stop> stops, int at)
    {
        var strip = stops[at].Strip;
        if (strip < 0)
        {
            return at;
        }

        var first = at;
        while (first > 0 && stops[first - 1].Strip == strip)
        {
            first--;
        }

        for (var chip = first; chip < stops.Count && stops[chip].Strip == strip; chip++)
        {
            if (stops[chip].Line.Selected == true)
            {
                return chip;
            }
        }

        return first;
    }

    /// <summary>Return presses the control. A switch is sent the other way round, and a slider or a stepper has no press.</summary>
    private static PanelStroke Return(PanelLine selected, bool held) => selected.Type switch
    {
        _ when held => new PanelStroke(true),
        Slider or Stepper => new PanelStroke(true),
        Switch => new PanelStroke(true, selected.Command, selected.Selected == true ? 0 : 1),
        _ => new PanelStroke(true, selected.Command),
    };

    /// <summary>Left and Right change a slider or a stepper. On any other control they are used and do nothing.</summary>
    private static PanelStroke Change(PanelLine selected, int direction)
    {
        if (selected.Type == Stepper)
        {
            return new PanelStroke(true, selected.Command, direction);
        }

        if (selected.Type != Slider)
        {
            return new PanelStroke(true);
        }

        var (minimum, maximum) = Range(selected);
        var now = Number(selected.Value) is { } value ? Math.Clamp(value, minimum, maximum) : minimum;
        var next = Math.Round(Math.Clamp(now + (direction * (maximum - minimum) / SliderSteps), minimum, maximum), 6);
        return next == now ? new PanelStroke(true) : new PanelStroke(true, selected.Command, next);
    }

    /// <summary>A slider carries its range in <see cref="PanelLine.Minimum"/> and <see cref="PanelLine.Maximum"/>. One that carries none, or an empty one, runs from 0 to 1.</summary>
    private static (double Minimum, double Maximum) Range(PanelLine slider)
    {
        var minimum = slider.Minimum ?? 0;
        var maximum = slider.Maximum ?? 1;
        return double.IsFinite(minimum) && double.IsFinite(maximum) && maximum > minimum ? (minimum, maximum) : (0, 1);
    }

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;

    private static List<Stop> Stops(IReadOnlyList<PanelLine> rows)
    {
        var stops = new List<Stop>();
        var strips = 0;
        Gather(rows, stops, -1, ref strips);
        return stops;
    }

    /// <summary>Every row a click could press, a row before the rows inside it. The rows of a chip row or a tab strip share its number.</summary>
    private static void Gather(IReadOnlyList<PanelLine> rows, List<Stop> into, int strip, ref int strips)
    {
        foreach (var row in rows)
        {
            if (!string.IsNullOrEmpty(row.Command))
            {
                into.Add(new Stop(row, strip));
            }

            if (row.Rows is { } inner)
            {
                Gather(inner, into, row.Type is Chips or Tabs ? strips++ : -1, ref strips);
            }
        }
    }

    /// <summary>A control with a command, and the row of chips it stands in, or -1.</summary>
    private readonly record struct Stop(PanelLine Line, int Strip);
}
