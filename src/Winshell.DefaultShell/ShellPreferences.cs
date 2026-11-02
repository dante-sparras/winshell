using System.ComponentModel;

namespace Winshell.DefaultShell;

/// <summary>
/// Applies Winshell Settings to the running shell. A choice changes Preferences only; this is what makes the shell
/// follow that file, including a value edited by hand.
/// </summary>
internal static class ShellPreferences
{
    public static void Follow(Shell shell, Preferences preferences)
    {
        preferences.PropertyChanged += (_, e) => Apply(shell, preferences, e.PropertyName);
        Apply(shell, preferences, null);
    }

    /// <summary><see cref="Preferences.ColorScheme"/>, or <c>system</c> when the file has one Winshell cannot use.</summary>
    internal static string Scheme(Preferences preferences) => preferences.ValidColorScheme;

    /// <summary><see cref="Preferences.AccentColor"/> uppercased, or <c>""</c> when it is not a color Winshell can use.</summary>
    internal static string Accent(Preferences preferences) =>
        preferences.ValidAccent ? preferences.AccentColor.ToUpperInvariant() : "";

    private static void Apply(Shell shell, Preferences preferences, string? property)
    {
        if (property is null or nameof(Preferences.ColorScheme))
            shell.ColorScheme = Scheme(preferences);
        if (property is null or nameof(Preferences.AccentColor))
            shell.AccentColor = Accent(preferences);
        if (property is null or nameof(Preferences.AutoUpdate))
            shell.Updates.Automatic = preferences.AutoUpdate;
        if (property is null or nameof(Preferences.HideExplorerTaskbar))
            shell.ExplorerTaskbar.Hidden = preferences.HideExplorerTaskbar;
    }
}
