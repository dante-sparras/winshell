namespace Winshell.Core;

/// <summary>One figure among a bar item's readouts, such as a processor icon and <c>15%</c>.</summary>
/// <param name="Label">The accessible name. Drawn when <paramref name="Glyph"/> is empty.</param>
/// <param name="Value">The figure beside the name or icon.</param>
/// <param name="Glyph">Named icon drawn instead of <paramref name="Label"/>. Empty draws the label.</param>
public sealed record BarReadout(string Label, string Value, string Glyph = "");
