using Microsoft.UI.Xaml;

namespace Winshell.Kit;

/// <summary>The Kit's bar controls, for an item a plugin draws itself (<see cref="IKitBar"/>).</summary>
public sealed partial class Kit
{
    /// <summary>
    /// A row of chips, such as the workspace numbers, with the accent line under the current one. A new current chip
    /// swaps two fills and slides the line; the chips are drawn again only when their numbers or icons change.
    /// </summary>
    /// <param name="chips">The chips now. Return the same list while nothing changed.</param>
    /// <param name="pick">Runs with a chip's tag when it is clicked.</param>
    /// <returns>The chips.</returns>
    public FrameworkElement BarChips(Func<IReadOnlyList<BarChip>> chips, Action<int> pick)
    {
        ArgumentNullException.ThrowIfNull(chips);
        ArgumentNullException.ThrowIfNull(pick);
        var strip = new ChipStrip(Vertical, tag => Pressed("chip:" + tag, () => pick(tag))());
        // A chip that names a monitor is marked on that monitor's Bar only.
        Bind<IReadOnlyList<BarChip>>(chips, shown => strip.Show(ChipBody.On(shown, Monitor)), ReferenceEqualityComparer.Instance);
        return strip.Element;
    }
}
