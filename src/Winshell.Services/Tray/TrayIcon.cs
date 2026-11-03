namespace Winshell.Tray;

/// <summary>One notification area icon an app added with <c>Shell_NotifyIcon</c>.</summary>
internal sealed class TrayIcon
{
    public required string Key { get; init; }

    public nint Window { get; set; }

    public uint Id { get; set; }

    public Guid Guid { get; set; }

    public uint CallbackMessage { get; set; }

    public uint Version { get; set; }

    public string Tip { get; set; } = "";

    /// <summary>The app hid the icon (<c>NIS_HIDDEN</c>); Explorer does not show it.</summary>
    public bool Hidden { get; set; }

    /// <summary>Windows shows the icon in the hidden icons flyout rather than in the taskbar.</summary>
    public bool Overflow { get; set; } = true;

    /// <summary>The icon as the app drew it, at the size it chose.</summary>
    public PixelImage? Image { get; set; }

    /// <summary>The module and resource the icon was loaded from, when Windows knows, so it can be drawn at any size.</summary>
    public IconResource? Resource { get; set; }

    public int ProcessId { get; set; }

    public string ProgramPath { get; set; } = "";
}
