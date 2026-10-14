using Microsoft.UI.Xaml;

namespace Winshell.Kit;

/// <summary>
/// A plugin that draws its own bar item with the Kit (ADR 0045), such as the workspace chips. Most plugins publish a
/// <see cref="Winshell.Core.BarFace"/> instead, and the host draws it as a <see cref="BarButton"/>.
/// A published face still shows or hides the item and sets its label and tooltip.
/// <para>
/// Every monitor has a Bar (ADR 0080), so the host builds the item once for each Bar, each time with that Bar's own <see cref="Kit"/>,
/// and reads the getters of each on every tick. It builds them again whenever the Bars are built again. An element belongs to one Bar,
/// so the plugin keeps none of what it returns: it keeps its state in fields, and every copy of the item reads that state
/// through getters (<see cref="Kit.Bind{T}"/>). A plugin that kept one element and wrote to it would change one Bar only.
/// </para>
/// </summary>
public interface IKitBar
{
    /// <summary>
    /// Builds the bar item for one Bar, and is called once for each Bar. <see cref="Kit.Vertical"/> says which way the bar runs.
    /// Return a new element on every call, built with <paramref name="kit"/> alone.
    /// </summary>
    /// <param name="kit">The Kit for this item on this Bar.</param>
    /// <returns>What the item shows on that Bar.</returns>
    UIElement BuildBar(Kit kit);
}
