namespace Winshell.DefaultShell.Taskbar;

/// <summary>A tile Winshell Settings and quick settings both know: its key, its name and its glyph.</summary>
public sealed record QuickSettingsKind(string Key, string Title, string Glyph)
{
    /// <summary>The tiles Winshell knows, in the order a missing list uses.</summary>
    public static IReadOnlyList<QuickSettingsKind> All { get; } =
    [
        new("wifi", "Wi-Fi", ""),
        new("bluetooth", "Bluetooth", ""),
        new("airplaneMode", "Airplane mode", ""),
        new("energySaver", "Energy saver", ""),
        new("powerMode", "Power mode", ""),
        new("nightLight", "Night light", ""),
        new("accessibility", "Accessibility", ""),
    ];
}
