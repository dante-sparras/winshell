namespace Winshell.Core;

/// <summary>A shown bar item as it lies along the bar.</summary>
/// <param name="Id">The id the item is shown under.</param>
/// <param name="Section"><c>left</c>, <c>center</c>, or <c>right</c>.</param>
/// <param name="Start">Where the item starts along the bar.</param>
/// <param name="End">Where it ends.</param>
public readonly record struct BarSpan(string Id, string Section, double Start, double End);

/// <summary>Where a dragged bar item lands.</summary>
/// <param name="Section">The section it joins.</param>
/// <param name="Before">The shown item it lands in front of, or null at the end of the section.</param>
/// <param name="After">The shown item it lands behind, or null at the start of the section.</param>
/// <param name="Mark">Where along the bar the insertion line is drawn.</param>
public readonly record struct BarDropTarget(string Section, string? Before, string? After, double Mark);

/// <summary>
/// A bar item dragged to another place on the bar. The pointer picks the section and the neighbours,
/// and the drop is written into the bar document the way <c>winoma bar move</c> writes it.
/// </summary>
public static class BarDrop
{
    /// <summary>Travel on either axis before a press on an item becomes a drag.</summary>
    public const double Threshold = 8;

    private const double Reach = 2;

    /// <summary>True when a press that has travelled this far is a drag and no longer a click.</summary>
    /// <param name="dx">Travel along the screen's x axis since the press.</param>
    /// <param name="dy">Travel along the screen's y axis since the press.</param>
    /// <returns>True once either axis reaches <see cref="Threshold"/>.</returns>
    public static bool Lifts(double dx, double dy) => Math.Abs(dx) >= Threshold || Math.Abs(dy) >= Threshold;

    /// <summary>True when the pointer has left the bar far enough that a release there drops nothing.</summary>
    /// <param name="across">The pointer across the bar, measured from the bar's own edge.</param>
    /// <param name="thickness">The bar's size across.</param>
    /// <returns>True beyond two thicknesses off either side.</returns>
    public static bool Away(double across, double thickness) =>
        across < -(Reach * thickness) || across > (Reach + 1) * thickness;

    /// <summary>
    /// Where <paramref name="dragged"/> lands when it is released at <paramref name="along"/>.
    /// The empty stretch between two sections is split down its middle, and an empty section starts at its own end of the bar.
    /// Inside the section the item goes in front of the first neighbour whose middle is past the pointer.
    /// </summary>
    /// <param name="items">Every shown item, the dragged one included, measured before the drag moved anything.</param>
    /// <param name="dragged">The id being dragged.</param>
    /// <param name="along">The pointer along the bar.</param>
    /// <param name="length">The bar's length.</param>
    /// <param name="gap">The space between two neighbours, which the insertion line sits in the middle of.</param>
    /// <returns>The place, or null when the item would stay where it is.</returns>
    public static BarDropTarget? Target(IReadOnlyList<BarSpan> items, string dragged, double along, double length, double gap)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(dragged);

        // This runs on every pointer move of a drag, so the spans are walked where they are and no list is made.
        var ordered = Ordered(items);
        var leftEnd = 0d;
        var centerStart = length / 2;
        var centerEnd = length / 2;
        var rightStart = length;
        var centerSeen = false;
        var rightSeen = false;
        for (var i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            if (item.Id.Equals(dragged, StringComparison.Ordinal))
            {
                continue;
            }

            if (In(item, "left"))
            {
                leftEnd = item.End;
            }
            else if (In(item, "center"))
            {
                if (!centerSeen)
                {
                    centerSeen = true;
                    centerStart = item.Start;
                }

                centerEnd = item.End;
            }
            else if (In(item, "right") && !rightSeen)
            {
                rightSeen = true;
                rightStart = item.Start;
            }
        }

        var section = along < (leftEnd + centerStart) / 2
            ? "left"
            : along < (centerEnd + rightStart) / 2 ? "center" : "right";

        // The neighbours are the section's other items. Home is the dragged item's place among all of the section's items.
        var at = 0;
        var home = -1;
        var place = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            if (!In(item, section))
            {
                continue;
            }

            if (item.Id.Equals(dragged, StringComparison.Ordinal))
            {
                if (home < 0)
                {
                    home = place;
                }
            }
            else if ((item.Start + item.End) / 2 < along)
            {
                at++;
            }

            place++;
        }

        if (home == at)
        {
            return null;
        }

        BarSpan? before = null;
        BarSpan? after = null;
        var neighbour = 0;
        for (var i = 0; i < ordered.Count && neighbour <= at; i++)
        {
            var item = ordered[i];
            if (!In(item, section) || item.Id.Equals(dragged, StringComparison.Ordinal))
            {
                continue;
            }

            if (neighbour == at - 1)
            {
                after = item;
            }
            else if (neighbour == at)
            {
                before = item;
            }

            neighbour++;
        }

        var mark = (before, after) switch
        {
            ({ } next, { } previous) => (previous.End + next.Start) / 2,
            ({ } next, null) => next.Start - (gap / 2),
            (null, { } previous) => previous.End + (gap / 2),
            _ => section switch
            {
                "left" => 0,
                "right" => length,
                _ => length / 2,
            },
        };
        return new BarDropTarget(section, before?.Id, after?.Id, mark);
    }

    /// <summary>
    /// The bar with the item shown as <paramref name="id"/> moved to <paramref name="target"/>.
    /// The result is the whole map, so a Host on the default bar gets the default bar with this one move.
    /// A clone moves the slot of the built-in it stands in for, and a plugin that had no slot gets one.
    /// A slot that is not shown, such as a disabled plugin's, keeps its place beside its neighbours.
    /// </summary>
    /// <param name="bar">The bar object from <c>settings.json</c>, or null for the default bar. It is not changed.</param>
    /// <param name="catalog">Discovered manifests, which name each clone's built-in.</param>
    /// <param name="id">The id the item is shown under.</param>
    /// <param name="target">Where it was dropped.</param>
    /// <returns>The edited copy.</returns>
    public static BarLayoutDocument Apply(BarLayoutDocument? bar, IReadOnlyList<PluginManifest> catalog, string id, BarDropTarget target)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(id);
        var editor = BarLayoutEditor.Copy(bar);
        editor.Drop(Held(id), target.Section, Neighbour(target.Before), Neighbour(target.After));
        return editor.Document;

        string? Neighbour(string? shown) => shown is null ? null : Held(shown);

        // The document names the built-in. The bar shows the clone that took its slot.
        string Held(string shown)
        {
            if (editor.Home(shown, out _) is not null)
            {
                return shown;
            }

            var source = catalog.FirstOrDefault(plugin => plugin.Id.Equals(shown, StringComparison.Ordinal))?.ClonedFrom ?? "";
            return source.Length > 0 && editor.Home(source, out _) is not null ? source : shown;
        }
    }

    /// <summary>The spans by where they start. The bar hands them over in that order already, and only a list out of order is copied and sorted.</summary>
    private static IReadOnlyList<BarSpan> Ordered(IReadOnlyList<BarSpan> items)
    {
        for (var i = 1; i < items.Count; i++)
        {
            if (items[i].Start < items[i - 1].Start)
            {
                return items.OrderBy(item => item.Start).ToList();
            }
        }

        return items;
    }

    private static bool In(BarSpan item, string section) => item.Section.Equals(section, StringComparison.Ordinal);
}
