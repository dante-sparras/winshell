namespace Winshell.Core;

/// <summary>
/// The hidden toast windows the bar keeps for the next toast. Opening one costs a frame, so a spare makes the next toast quick,
/// but each holds a whole window. After a minute with no toast in or out, all but <see cref="Kept"/> close.
/// </summary>
public static class ToastSpares
{
    /// <summary>The spares kept after a quiet minute.</summary>
    public const int Kept = 1;

    /// <summary>How long the toasts must be quiet before spares close.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(1);

    /// <summary>How many of <paramref name="spare"/> hidden windows to close.</summary>
    /// <param name="spare">Hidden toast windows waiting for reuse.</param>
    /// <param name="quietFor">How long since a toast last came or went.</param>
    /// <param name="monitors">How many monitors draw toasts. A toast is a window on each, so that many spares make the next toast quick.</param>
    /// <returns>Zero before the quiet minute is up.</returns>
    public static int ToClose(int spare, TimeSpan quietFor, int monitors = 1) =>
        quietFor >= Quiet ? Math.Max(0, spare - (Kept * Math.Max(1, monitors))) : 0;
}
