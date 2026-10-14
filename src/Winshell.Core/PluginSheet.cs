using System.Text.Json;

namespace Winshell.Core;

/// <summary>The face and panel a sheet plugin draws, read from <c>sheet</c> in its manifest.</summary>
public sealed class PluginSheet
{
    /// <summary>The one command an action row can send: it closes the panel.</summary>
    public const string Close = "close";

    /// <summary>Creates a sheet.</summary>
    /// <param name="text">Bar label.</param>
    /// <param name="glyph">Named icon. Empty draws none.</param>
    /// <param name="tooltip">Hover text. Empty shows none.</param>
    /// <param name="accent">True uses the accent color.</param>
    /// <param name="title">Panel title.</param>
    /// <param name="rows">Panel rows from top to bottom.</param>
    public PluginSheet(string text, string glyph, string tooltip, bool accent, string title, IReadOnlyList<SheetRow> rows)
    {
        Text = text;
        Glyph = glyph;
        Tooltip = tooltip;
        Accent = accent;
        Title = title;
        Rows = rows;
    }

    /// <summary>Bar label.</summary>
    public string Text { get; }

    /// <summary>Named icon. Empty draws none.</summary>
    public string Glyph { get; }

    /// <summary>Hover text. Empty shows none.</summary>
    public string Tooltip { get; }

    /// <summary>True uses the accent color.</summary>
    public bool Accent { get; }

    /// <summary>Panel title.</summary>
    public string Title { get; }

    /// <summary>Panel rows from top to bottom.</summary>
    public IReadOnlyList<SheetRow> Rows { get; }

    /// <summary>Reads a <c>sheet</c> object.</summary>
    /// <param name="sheet">The <c>sheet</c> property.</param>
    /// <param name="id">Manifest id, used in an error.</param>
    /// <param name="name">Display name. The label and title use it when the sheet omits them.</param>
    /// <param name="parsed">The sheet, or null when this returns false.</param>
    /// <param name="error">The sentence when this returns false, or null.</param>
    /// <returns>False when the object is not a sheet.</returns>
    public static bool TryRead(JsonElement sheet, string id, string name, out PluginSheet? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (sheet.ValueKind != JsonValueKind.Object)
        {
            error = id + " sheet must be an object";
            return false;
        }

        if (!Optional(sheet, "text", id, out var text, out error)
            || !Optional(sheet, "glyph", id, out var glyph, out error)
            || !Optional(sheet, "tooltip", id, out var tooltip, out error)
            || !Optional(sheet, "title", id, out var title, out error))
        {
            return false;
        }

        if (sheet.TryGetProperty("accent", out var accent) && accent.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = id + " sheet accent must be true or false";
            return false;
        }

        if (!TryRows(sheet, id, out var rows, out error))
        {
            return false;
        }

        var label = text.Length > 0 ? text : name;
        parsed = new PluginSheet(
            label,
            glyph,
            tooltip,
            accent.ValueKind == JsonValueKind.True,
            title.Length > 0 ? title : name,
            rows);
        return true;
    }

    private static bool Optional(JsonElement sheet, string key, string id, out string value, out string? error)
    {
        value = "";
        error = null;
        if (!sheet.TryGetProperty(key, out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = id + " sheet " + key + " must be a string";
            return false;
        }

        value = element.GetString() ?? "";
        return true;
    }

    private static bool TryRows(JsonElement sheet, string id, out IReadOnlyList<SheetRow> rows, out string? error)
    {
        rows = [];
        error = null;
        if (!sheet.TryGetProperty("rows", out var array))
        {
            return true;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            error = id + " sheet rows must be an array";
            return false;
        }

        var list = new List<SheetRow>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = id + " sheet row " + index + " must be an object";
                return false;
            }

            var text = item.TryGetProperty("text", out _);
            var action = item.TryGetProperty("action", out _);
            var section = item.TryGetProperty("section", out _);
            var rule = item.TryGetProperty("rule", out _);
            var kinds = (text ? 1 : 0) + (action ? 1 : 0) + (section ? 1 : 0) + (rule ? 1 : 0);
            if (kinds == 0)
            {
                error = id + " sheet row " + index + " needs text, action, section, or rule";
                return false;
            }

            if (kinds > 1)
            {
                error = id + " sheet row " + index + " has more than one kind";
                return false;
            }

            if (text)
            {
                if (!Line(item, "text", id, index, out var value, out error))
                {
                    return false;
                }

                list.Add(new SheetRow.Text(value));
                continue;
            }

            if (section)
            {
                if (!Line(item, "section", id, index, out var value, out error))
                {
                    return false;
                }

                list.Add(new SheetRow.Section(value));
                continue;
            }

            if (action)
            {
                if (!Line(item, "action", id, index, out var label, out error) || !Line(item, "command", id, index, out var command, out error))
                {
                    return false;
                }

                // A sheet has no code, so the one thing an action can ask for is to close its panel. Any other command would draw a button that does nothing.
                if (!command.Equals(Close, StringComparison.OrdinalIgnoreCase))
                {
                    error = id + " sheet row " + index + " command must be " + Close;
                    return false;
                }

                list.Add(new SheetRow.Action(label, command));
                continue;
            }

            if (item.GetProperty("rule").ValueKind != JsonValueKind.True)
            {
                error = id + " sheet row " + index + " rule must be true";
                return false;
            }

            list.Add(new SheetRow.Rule());
        }

        rows = list;
        return true;
    }

    private static bool Line(JsonElement item, string key, string id, int index, out string value, out string? error)
    {
        value = "";
        error = null;
        if (!item.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            error = id + " sheet row " + index + " " + key + " must be text";
            return false;
        }

        value = element.GetString() ?? "";
        return true;
    }
}

/// <summary>One row in a <see cref="PluginSheet"/>.</summary>
public abstract record SheetRow
{
    /// <summary>A line of text.</summary>
    /// <param name="Value">The line.</param>
    public sealed record Text(string Value) : SheetRow;

    /// <summary>A button that sends <paramref name="Command"/>.</summary>
    /// <param name="Label">Button text.</param>
    /// <param name="Command">Command name. <c>close</c> dismisses the panel.</param>
    public sealed record Action(string Label, string Command) : SheetRow;

    /// <summary>A section label.</summary>
    /// <param name="Label">The label.</param>
    public sealed record Section(string Label) : SheetRow;

    /// <summary>A hairline between groups.</summary>
    public sealed record Rule : SheetRow;
}
