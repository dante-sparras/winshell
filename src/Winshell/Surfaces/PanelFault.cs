namespace Winshell;

/// <summary>Panel and plugin stacks are contained. A shell stack stays fatal.</summary>
internal static class PanelFault
{
    /// <summary>True when <paramref name="stack"/> names the panel or a type under <c>Winoma.Plugins</c>.</summary>
    /// <param name="stack">A stack trace from the exception or an inner exception.</param>
    public static bool IsContained(string? stack) =>
        stack is not null
        && (stack.Contains("PanelWindow", StringComparison.Ordinal)
            || stack.Contains("Winoma.Plugins.", StringComparison.Ordinal)
            || stack.Contains("Winshell.Kit.Kit", StringComparison.Ordinal));
}

