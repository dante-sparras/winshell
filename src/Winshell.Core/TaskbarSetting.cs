namespace Winshell.Core;

/// <summary>
/// Whether the shell hides the Windows taskbar while it runs.
/// The word is <see cref="Hidden"/> or <see cref="Shown"/>. Anything else, including a missing key, is shown.
/// </summary>
public static class TaskbarSetting
{
    /// <summary>The Windows taskbar stays on screen.</summary>
    public const string Shown = "shown";

    /// <summary>The shell hides the Windows taskbar while it runs, and shows it again when the shell stops.</summary>
    public const string Hidden = "hidden";

    /// <summary>True when <paramref name="value"/> asks the shell to hide the Windows taskbar.</summary>
    /// <param name="value">The <c>taskbar</c> word from the shell's settings.</param>
    /// <returns>True only for <see cref="Hidden"/>.</returns>
    public static bool Hides(string? value) => string.Equals(value, Hidden, StringComparison.Ordinal);
}
