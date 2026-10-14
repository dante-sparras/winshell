namespace Winshell.Core;

/// <summary>The open panel's rows as <c>winshell state</c> prints them.</summary>
public static class ShellRows
{
    /// <summary>One row: its type, then its label, then its command, each only when it has one.</summary>
    /// <param name="line">A row of the open panel.</param>
    /// <returns>The text after <c>row</c>.</returns>
    public static string Text(PanelLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = line.Type;
        if (!string.IsNullOrEmpty(line.Label))
        {
            text += " " + line.Label;
        }

        if (!string.IsNullOrEmpty(line.Command))
        {
            text += " " + line.Command;
        }

        return text;
    }

    /// <summary>Each row of <paramref name="rows"/>, top to bottom. Null is none.</summary>
    /// <param name="rows">The open panel's rows.</param>
    /// <returns>The texts.</returns>
    public static IReadOnlyList<string> Texts(IReadOnlyList<PanelLine>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return [];
        }

        var texts = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            texts.Add(Text(row));
        }

        return texts;
    }
}
