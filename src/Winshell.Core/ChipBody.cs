namespace Winshell.Core;

/// <summary>Compares workspace chips without hashing their icon bytes.</summary>
public static class ChipBody
{
    /// <summary>
    /// True when <paramref name="left"/> and <paramref name="right"/> are the same buttons.
    /// Accent is ignored. Icon lists match when they hold the same byte arrays.
    /// </summary>
    /// <param name="left">Chips already drawn.</param>
    /// <param name="right">Chips a new face would draw.</param>
    /// <returns>True when a retint is enough.</returns>
    public static bool Same(IReadOnlyList<BarChip> left, IReadOnlyList<BarChip> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].Tag != right[i].Tag || left[i].Text != right[i].Text || !SameIcons(left[i].Icons, right[i].Icons))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The chips as the Bar on <paramref name="monitor"/> draws them (ADR 0080): a chip that names another monitor loses its accent,
    /// so each Bar marks the workspace in front on its own monitor.
    /// </summary>
    /// <param name="chips">The chips a plugin published, the same for every Bar.</param>
    /// <param name="monitor">The monitor the Bar is on, as <c>winoma desktop</c> numbers the monitors.</param>
    /// <returns><paramref name="chips"/> itself when no chip changes.</returns>
    public static IReadOnlyList<BarChip> On(IReadOnlyList<BarChip> chips, int monitor)
    {
        ArgumentNullException.ThrowIfNull(chips);
        List<BarChip>? own = null;
        for (var i = 0; i < chips.Count; i++)
        {
            var chip = chips[i];
            if (chip.Accent && chip.Monitor is int named && named != monitor)
            {
                own ??= [.. chips];
                own[i] = chip with { Accent = false };
            }
        }

        return own ?? chips;
    }

    /// <summary>True when each chip's accent matches, in order.</summary>
    /// <param name="left">Chips already drawn.</param>
    /// <param name="right">Chips a new face would draw.</param>
    /// <returns>True when the highlight does not move.</returns>
    public static bool SameAccent(IReadOnlyList<BarChip> left, IReadOnlyList<BarChip> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].Accent != right[i].Accent)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameIcons(IReadOnlyList<byte[]> left, IReadOnlyList<byte[]> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }
}
