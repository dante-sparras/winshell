using System.Text.Json.Serialization;

namespace Winshell.Core;

/// <summary>
/// One row of the open panel, as text for <c>winoma desktop</c>: what it is, what it says, and what a click on it sends.
/// An agent reads these to see a panel and to pick the command <c>winoma summon</c> sends.
/// </summary>
/// <param name="Type">What the row is, such as <c>heading</c>, <c>slider</c>, <c>switch</c>, <c>option</c>, or <c>section</c>.</param>
/// <param name="Label">What the row says. Null when it says nothing.</param>
/// <param name="Value">Its value: a slider's number, a figure, a field's text, a stepper's setting. Null when it has none.</param>
/// <param name="Detail">A quieter line under it, such as a status. Null when it has none.</param>
/// <param name="Command">What a click sends. A slider or switch sends it with a value, a stepper with -1 or 1, a field with its text. Null when the row does nothing.</param>
/// <param name="Selected">True for the chosen option, an on switch, or the current device. Null when the row cannot be chosen.</param>
/// <param name="Rows">The rows inside it, such as a tab strip's tabs or a card's rows. Null when it holds none.</param>
/// <param name="Minimum">The least a slider's value can be. Null for a row that is no slider. A slider that gives none runs from 0.</param>
/// <param name="Maximum">The most a slider's value can be. Null for a row that is no slider. A slider that gives none runs to 1.</param>
public sealed record PanelLine(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Command = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Selected = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PanelLine>? Rows = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Minimum = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Maximum = null);

/// <summary>The color a meter or a note uses.</summary>
public enum PanelTone
{
    /// <summary>The accent for a fill, muted for text.</summary>
    Normal,

    /// <summary>Windows' success green.</summary>
    Healthy,

    /// <summary>Amber.</summary>
    Warn,

    /// <summary>Windows' critical red.</summary>
    Critical,
}

/// <summary>A tab, chip, or button in a row of choices.</summary>
/// <param name="Label">What the chip says.</param>
/// <param name="Glyph">Icon name. Empty draws no icon.</param>
/// <param name="Command">Click command.</param>
/// <param name="Selected">True for the chosen tab or chip.</param>
public sealed record PanelChip(string Label, string Glyph, string Command, bool Selected = false);

/// <summary>One cell in a month grid.</summary>
/// <param name="Date">The calendar day this cell shows.</param>
/// <param name="InMonth">False when the day belongs to the previous or next month.</param>
/// <param name="Today">True when this cell is the current day.</param>
public readonly record struct CalendarDay(DateOnly Date, bool InMonth, bool Today);
