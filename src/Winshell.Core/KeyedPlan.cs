namespace Winshell.Core;

/// <summary>
/// The edits that turn the items of a keyed list on screen into the items the plugin returns now, matched by key.
/// Removals run first, from the highest index, so a later index still names the same item.
/// Then each desired item is inserted, or kept and moved to its place, and an item whose value changed is built again.
/// </summary>
public sealed class KeyedPlan
{
    /// <summary>What happens to one desired item.</summary>
    public enum Step
    {
        /// <summary>The item is on screen and unchanged.</summary>
        Keep,

        /// <summary>The item is on screen but its value changed. Build it again in its place.</summary>
        Replace,

        /// <summary>The item is new. Build it and insert it.</summary>
        Insert,
    }

    /// <summary>Where one desired item goes after the removals and the earlier slots.</summary>
    /// <param name="Index">Its index once this slot is done.</param>
    /// <param name="From">Where the surviving item sits before this slot. Null when it is inserted.</param>
    /// <param name="Kind">What happens to it.</param>
    public readonly record struct Slot(int Index, int? From, Step Kind);

    private KeyedPlan(int[] removals, Slot[] slots)
    {
        Removals = removals;
        Slots = slots;
    }

    /// <summary>Indexes on screen to remove, highest first.</summary>
    public IReadOnlyList<int> Removals { get; }

    /// <summary>One slot per desired item, in order.</summary>
    public IReadOnlyList<Slot> Slots { get; }

    /// <summary>True when every item stays where it is, unchanged.</summary>
    public bool Unchanged => Removals.Count == 0 && Slots.All(slot => slot.Kind == Step.Keep && slot.From == slot.Index);

    /// <summary>Plans the edits. Two items with the same key are told apart by their order.</summary>
    /// <param name="shown">Keys on screen, in order.</param>
    /// <param name="shownValues">What each item on screen was built from.</param>
    /// <param name="desired">Keys the list should show, in order.</param>
    /// <param name="desiredValues">What each desired item is built from.</param>
    /// <param name="same">True when an item built from the first value still shows the second.</param>
    /// <returns>The removals, then one slot per desired item.</returns>
    public static KeyedPlan Build<T>(IReadOnlyList<string> shown, IReadOnlyList<T> shownValues, IReadOnlyList<string> desired, IReadOnlyList<T> desiredValues, Func<T, T, bool> same)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(shownValues);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(desiredValues);
        ArgumentNullException.ThrowIfNull(same);
        var have = Identities(shown);
        var want = Identities(desired);
        var wanted = new HashSet<string>(want, StringComparer.Ordinal);
        var removals = new List<int>();
        var survivors = new List<int>(shown.Count);
        for (var i = 0; i < shown.Count; i++)
        {
            survivors.Add(i);
        }

        for (var i = survivors.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(have[i]))
            {
                removals.Add(i);
                survivors.RemoveAt(i);
            }
        }

        var slots = new List<Slot>(desired.Count);
        for (var i = 0; i < desired.Count; i++)
        {
            var existing = survivors.FindIndex(index => index >= 0 && string.Equals(have[index], want[i], StringComparison.Ordinal));
            if (existing < 0)
            {
                // An inserted item holds its place with an index nothing on screen has.
                survivors.Insert(Math.Min(i, survivors.Count), -1);
                slots.Add(new Slot(i, null, Step.Insert));
                continue;
            }

            var original = survivors[existing];
            if (existing != i)
            {
                survivors.RemoveAt(existing);
                survivors.Insert(i, original);
            }

            var kind = same(shownValues[original], desiredValues[i]) ? Step.Keep : Step.Replace;
            slots.Add(new Slot(i, existing, kind));
        }

        return new KeyedPlan(removals.ToArray(), slots.ToArray());
    }

    private static string[] Identities(IReadOnlyList<string> keys)
    {
        var identities = new string[keys.Count];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            seen.TryGetValue(keys[i], out var count);
            seen[keys[i]] = count + 1;
            identities[i] = count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + keys[i];
        }

        return identities;
    }
}
