namespace Winshell.Core;

/// <summary>One labeled figure, such as a battery's size.</summary>
/// <param name="Label">What the figure is.</param>
/// <param name="Value">The figure, such as <c>52 Wh</c>.</param>
public sealed record PanelFigure(string Label, string Value);
