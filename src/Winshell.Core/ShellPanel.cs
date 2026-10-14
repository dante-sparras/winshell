namespace Winshell.Core;

/// <summary>The panel on screen, for a desktop reply.</summary>
/// <param name="Id">The plugin whose panel it is.</param>
/// <param name="Rows">The panel's Kit controls as text. Null before it has drawn.</param>
/// <param name="Selected">The command of the control the arrow keys are on. Null when they are on none.</param>
/// <param name="Screen">The monitor the panel is on, whole, in physical pixels. Null when the caller does not know it.</param>
public sealed record ShellPanel(string Id, IReadOnlyList<PanelLine>? Rows, string? Selected, ShellRect? Screen);

/// <summary>What the shell asks an extension when it looks at one bar's layer.</summary>
/// <param name="Window">The bar window.</param>
/// <param name="Screen">The monitor the bar is on, whole, in physical pixels.</param>
/// <param name="Covered">True while the bar is behind a window.</param>
/// <param name="Behind">True while the window was written as not topmost.</param>
/// <param name="CoveredBy">The window the bar went behind, or zero.</param>
public readonly record struct BarLayerQuery(nint Window, ShellRect Screen, bool Covered, bool Behind, nint CoveredBy);

/// <summary>Where one bar's layer landed.</summary>
/// <param name="Covered">True while the bar is behind a window.</param>
/// <param name="Behind">True while the window is written as not topmost.</param>
/// <param name="CoveredBy">The window the bar went behind, or zero.</param>
/// <param name="PanelAbove">True while a panel on this bar stays above other windows.</param>
public readonly record struct BarYield(bool Covered, bool Behind, nint CoveredBy, bool PanelAbove);

/// <summary>A panel or plugin stack is contained. A shell stack stays fatal.</summary>
public static class PanelFaults
{
    /// <summary>True when <paramref name="stack"/> names the panel, the Kit, or a type under <c>Winoma.Plugins</c>.</summary>
    /// <param name="stack">A stack trace from the exception or an inner exception.</param>
    /// <returns>True when the fault stays in the panel.</returns>
    public static bool IsContained(string? stack) =>
        stack is not null
        && (stack.Contains("PanelWindow", StringComparison.Ordinal)
            || stack.Contains("Winoma.Plugins.", StringComparison.Ordinal)
            || stack.Contains("Winshell.Kit.Kit", StringComparison.Ordinal));
}
