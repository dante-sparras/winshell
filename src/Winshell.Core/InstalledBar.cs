namespace Winshell.Core;

/// <summary>The bar a product uses when <c>settings.json</c> has no bar object.</summary>
internal static class InstalledBar
{
    /// <summary>
    /// Builds that bar. The product installs this once. While it is null, a missing bar is an empty document.
    /// </summary>
    internal static Func<BarLayoutDocument>? Create { get; set; }

    /// <summary>The installed bar, or an empty document when none is installed.</summary>
    /// <returns>The document.</returns>
    internal static BarLayoutDocument OrEmpty() => Create?.Invoke() ?? new BarLayoutDocument();
}
