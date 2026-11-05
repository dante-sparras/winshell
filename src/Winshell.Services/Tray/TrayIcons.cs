namespace Winshell.Tray;

/// <summary>
/// The icons apps added with <c>Shell_NotifyIcon</c>, kept the way Explorer keeps them: one icon whether an app names
/// it by GUID or by window and id, and in the hidden icons flyout as Windows' settings choose.
/// </summary>
/// <param name="readIcon">Reads an app's icon handle while the app still has it.</param>
/// <param name="ownerOf">The process that owns a window, and its program's path.</param>
internal sealed class TrayIcons(
    Func<nint, (PixelImage? Image, IconResource? Resource)> readIcon,
    Func<nint, (int ProcessId, string ProgramPath)> ownerOf)
{
    private const uint WM_USER = 0x0400;
    private readonly Dictionary<string, TrayIcon> _icons = [];
    private readonly Dictionary<(nint Window, uint Id), TrayIcon> _byWindow = [];
    private readonly Dictionary<Guid, TrayIcon> _byGuid = [];
    private IReadOnlyList<TrayIconSetting> _settings = [];

    public IReadOnlyCollection<TrayIcon> Icons => _icons.Values;

    public event Action<TrayIcon>? Added;

    public event Action<TrayIcon>? Changed;

    public event Action<TrayIcon>? Removed;

    /// <summary>The app asked for a balloon notification: its title, its text, and its <c>NIIF_</c> flags.</summary>
    public event Action<TrayIcon, string, string, uint>? Balloon;

    /// <summary>
    /// Applies a call. A change to an icon this table does not have fails, as in Explorer, so an app that changes its icon
    /// when asked to add it again, as Windows Security does, adds it. Every other call succeeds.
    /// </summary>
    public bool Apply(NotifyIconCall call)
    {
        var hasGuid = call.Guid != Guid.Empty;
        var window = call.Window;
        // NIM_SETVERSION often leaves the GUID flag off, so an icon added by GUID is also found by its window.
        var icon = Lookup(window, call.Id, call.Guid);
        var key = icon?.Key ?? (hasGuid ? call.Guid.ToString("N") : $"{(uint)window:x}-{call.Id}");
        switch (call.Message)
        {
            case TrayProtocol.Add:
            case TrayProtocol.Modify:
            {
                if (icon is null && call.Message == TrayProtocol.Modify)
                    return false;
                icon ??= new TrayIcon { Key = key };
                Unindex(icon);
                if (window != 0)
                {
                    icon.Window = window;
                    icon.Id = call.Id;
                }

                if (hasGuid)
                    icon.Guid = call.Guid;
                // A system message such as WM_CLOSE is no app's callback, and is never sent.
                if ((call.Flags & TrayProtocol.NIF_MESSAGE) != 0)
                    icon.CallbackMessage = call.CallbackMessage >= WM_USER ? call.CallbackMessage : 0;
                if ((call.Flags & TrayProtocol.NIF_TIP) != 0)
                    icon.Tip = call.Tip;
                if ((call.Flags & TrayProtocol.NIF_STATE) != 0 && (call.StateMask & TrayProtocol.NIS_HIDDEN) != 0)
                    icon.Hidden = (call.State & TrayProtocol.NIS_HIDDEN) != 0;
                if ((call.Flags & TrayProtocol.NIF_ICON) != 0)
                    // The app may destroy the icon once this returns, so it is read now.
                    (icon.Image, icon.Resource) = readIcon(call.Icon);
                Index(icon);
                var added = !_icons.ContainsKey(icon.Key);
                if (added)
                {
                    (icon.ProcessId, icon.ProgramPath) = ownerOf(icon.Window);
                    _icons[icon.Key] = icon;
                }

                icon.Overflow = TrayIconSettings.InOverflow(_settings, icon.Guid, icon.ProgramPath, icon.Id);
                if (added)
                    Added?.Invoke(icon);
                else
                    Changed?.Invoke(icon);
                // An empty text takes a balloon down; Explorer does that itself.
                if ((call.Flags & TrayProtocol.NIF_INFO) != 0 && call.Info.Length > 0)
                    Balloon?.Invoke(icon, call.InfoTitle, call.Info, call.InfoFlags);
                break;
            }
            case TrayProtocol.Delete:
                if (icon is not null)
                    Remove(icon.Key);
                break;
            case TrayProtocol.SetVersion:
                // The only message where this field is a version; elsewhere it is a balloon timeout.
                if (icon is not null)
                    icon.Version = call.TimeoutOrVersion;
                break;
        }

        return true;
    }

    /// <summary>Takes Windows' choices for which icons the taskbar shows, and updates the icons they changed.</summary>
    public void UseSettings(IReadOnlyList<TrayIconSetting> settings)
    {
        _settings = settings;
        foreach (var icon in _icons.Values.ToArray())
        {
            var overflow = TrayIconSettings.InOverflow(settings, icon.Guid, icon.ProgramPath, icon.Id);
            if (overflow == icon.Overflow)
                continue;
            icon.Overflow = overflow;
            Changed?.Invoke(icon);
        }
    }

    public void Remove(string key)
    {
        if (!_icons.Remove(key, out var icon))
            return;
        Unindex(icon);
        Removed?.Invoke(icon);
    }

    /// <summary>An icon added by GUID and later addressed by its window, or the other way around, is the same icon.</summary>
    private TrayIcon? Lookup(nint window, uint id, Guid guid)
    {
        if (guid != Guid.Empty && _byGuid.TryGetValue(guid, out var byGuid))
            return byGuid;
        if (window != 0 && _byWindow.TryGetValue((window, id), out var byWindow))
            return byWindow;
        return null;
    }

    private void Index(TrayIcon icon)
    {
        if (icon.Window != 0)
            _byWindow[(icon.Window, icon.Id)] = icon;
        if (icon.Guid != Guid.Empty)
            _byGuid[icon.Guid] = icon;
    }

    private void Unindex(TrayIcon icon)
    {
        if (icon.Window != 0 && _byWindow.TryGetValue((icon.Window, icon.Id), out var byWindow) &&
            ReferenceEquals(byWindow, icon))
            _byWindow.Remove((icon.Window, icon.Id));
        if (icon.Guid != Guid.Empty && _byGuid.TryGetValue(icon.Guid, out var byGuid) && ReferenceEquals(byGuid, icon))
            _byGuid.Remove(icon.Guid);
    }
}
