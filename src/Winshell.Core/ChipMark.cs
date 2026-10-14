namespace Winshell.Core;

/// <summary>Identity of the chips a bar slot is showing, including which icons.</summary>
public static class ChipMark
{
    // A picture's bytes do not change while its array lives, so each array is walked once. The table keeps no array alive.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], System.Runtime.CompilerServices.StrongBox<int>> Mixed = new();

    /// <summary>
    /// A stable mark of <paramref name="chips"/>. Two slots with the same icon count and different icons do not match.
    /// </summary>
    /// <param name="chips">Chips the slot would draw.</param>
    /// <returns>The mark.</returns>
    public static string Of(IReadOnlyList<BarChip> chips)
    {
        ArgumentNullException.ThrowIfNull(chips);
        if (chips.Count == 0)
        {
            return "";
        }

        var builder = new System.Text.StringBuilder();
        foreach (var chip in chips)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            builder.Append(chip.Tag);
            builder.Append(chip.Accent ? '*' : '.');
            foreach (var icon in chip.Icons)
            {
                builder.Append(':');
                builder.Append(icon.Length);
                builder.Append('x');
                builder.Append(Mixed.GetValue(icon, static bytes => new(Mix(bytes))).Value);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// A stable mark of one picture. Empty when there is none.
    /// The bytes are walked once while the array lives, the same way a chip icon's are.
    /// </summary>
    /// <param name="picture">PNG bytes, or null.</param>
    /// <returns>The mark.</returns>
    public static string Picture(byte[]? picture)
    {
        if (picture is not { Length: > 0 })
        {
            return "";
        }

        return picture.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "x"
            + Mixed.GetValue(picture, static bytes => new System.Runtime.CompilerServices.StrongBox<int>(Mix(bytes))).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // A span walk. The interface indexer it replaced was one virtual call per icon byte, on every bar paint.
    private static int Mix(ReadOnlySpan<byte> icon)
    {
        var hash = 17;
        foreach (var value in icon)
        {
            hash = unchecked((hash * 31) + value);
        }

        return hash;
    }
}
